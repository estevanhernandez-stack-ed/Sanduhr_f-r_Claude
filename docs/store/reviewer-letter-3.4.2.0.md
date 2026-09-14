# Notes for certification — reviewer letter (v3.4.2.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; front-loaded per playbook gotcha #8). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: v3.4.1.0 is the approved current version. v3.4.2 is a maintenance release with **no
> new outbound destination and no new at-rest data**. The one thing worth a reviewer's eye is
> that the app now asks the Store itself whether an update is waiting — a Windows platform call
> (`StoreContext.GetAppAndOptionalStorePackageUpdatesAsync`), no third-party endpoint — so it can
> offer to quit and let the install finish. The letter leads with that, states plainly that
> nothing else on the disclosure surface moved, and carries the 10.1.4.4 (a)(b)(c) statements and
> the trademark disclaimer forward.

---

```
Hello reviewer,

This updates the approved v3.4.1.0 — same Identity
(626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing, same
package contents. Declares ONLY runFullTrust. No telemetry.

Nothing moved on the disclosure surface: no new at-rest data, and no new
outbound destination — a default install still contacts only claude.ai.

One addition worth naming, because it is a network call: the app now asks
the Store whether an update to itself is waiting, through the Windows
platform API (StoreContext.GetAppAndOptionalStorePackageUpdatesAsync), at
most once every six hours. It talks to the Store, not to us; we operate no
update service and receive nothing from it. When an update is waiting the
app says so and offers a button that opens the Store's downloads page and
closes the app. This exists because an MSIX cannot replace files a running
process holds: an update attempted while the widget runs fails, and the
user is left on the old build with no explanation.

Also in this release: a session that Claude Code opened in a subfolder of
a repository now counts toward that repository in the app's own local
usage figures, and the theme editor in Settings scrolls so its Save button
stays reachable on a short window. Both are local-only.

Preserved (10.1.4.4): (a) trademark disclaimer on every surface naming
Claude, specific copyright entity; (b) burn-rate projection and pacing,
focus hourglass, cooldown game, themes, Mica, Credential Manager storage;
(c) tool-strip navigation with accessible names, shortcuts in
Settings > Help, native close, dialogs legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with, endorsed by, or associated with Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## If the field is shorter (sub-1000 char fallback)

```
Updates the approved v3.4.1.0 — same Identity (626LabsLLC.SanduhrfrClaude)
and Publisher CN, same package contents. No telemetry; declares ONLY
runFullTrust. No new at-rest data and no new outbound destination: a
default install still contacts only claude.ai.

One addition worth naming: the app now asks the Store whether an update to
itself is waiting, through the Windows platform API (StoreContext), at
most every six hours, and offers to close so the install can finish. It
talks to the Store, not to us. An MSIX cannot replace files a running
process holds, so an update attempted while the app runs fails silently;
this tells the user why.

Otherwise local-only fixes: usage figures attribute a subfolder session to
its repository, and the Settings theme editor scrolls so Save stays
reachable.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.4.2.0)

- [ ] Version trio in csproj + `<Identity Version>` = `3.4.2.0` (4th component `.0`); `Sanduhr.Mcp.csproj` at 3.4.2
- [ ] `dist/Sanduhr-Store-v3.4.2.0.msix` built off the `v3.4.2` tag, unsigned; `mcp\sanduhr-mcp.exe` present in the staged payload and `scripts/smoke-mcp.ps1` passes against `dist/publish/mcp/sanduhr-mcp.exe`
- [ ] ONLY `runFullTrust` declared; trademark disclaimer on all six surfaces (Store description,
      Copyright field, manifest Description, privacy policy, README, About box)
- [ ] **Support contact set to `support@626labs.dev`** on the Properties page (the API's
      `supportContact` field is obsolete, so this is hand-only and publishes with this submission)
- [ ] **Store update notice verified on the MSIX build:** with an update genuinely waiting the
      status line and toast appear and the button closes the app; with none, nothing is shown and
      the log stays quiet. Inert on the Velopack build (no Store identity).
- [ ] **Theme editor scroll verified:** Settings ▸ Themes ▸ Studio at the default window height —
      "Save & apply" is reachable without resizing; installed themes and their buttons stay pinned
- [ ] `docs/PRIVACY.md` unchanged this release (no new data, no new destination); the hosted policy
      at 626labs.dev/privacy.html#privacy-sanduhr needs no edit
- [ ] "What's new" through the Store Listing Console from
      `store-listing-console/apps/sanduhr/copy/whats-new-3.4.2.0.md`;
      Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green (Sanduhr.Tests 704, Sanduhr.Mcp.Tests 87)

## Source

Supersedes [`reviewer-letter-3.4.1.0.md`](./reviewer-letter-3.4.1.0.md). Public listing copy for
this version is written from `store-listing-console/apps/sanduhr/copy/` and archived here as
`listing-copy-3.4.2.md` after certification (runbook Phase 7, step 8). GitHub release notes:
[`release-notes-3.4.2.0.md`](./release-notes-3.4.2.0.md).
