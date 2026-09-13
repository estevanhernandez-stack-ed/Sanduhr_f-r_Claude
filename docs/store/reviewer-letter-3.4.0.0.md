# Notes for certification — reviewer letter (v3.4.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; front-loaded per playbook gotcha #8). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: v3.3.0.0 is the approved current version. v3.4.0 has **one disclosure-surface
> change**: an optional outbound destination that the user configures ("Publish usage"), off by
> default, nothing sent until the user turns it on, sets an endpoint, and ticks a Claude Code
> home. The letter leads with it (runbook Phase 2 rule), gives the exact in-app explainer
> sentence, then covers the local snapshot write and the `publish_usage` handoff, then carries the
> 10.1.4.4 (a)(b)(c) statements and the trademark disclaimer forward.

---

```
Hello reviewer,

This updates the approved v3.3.0.0 — same Identity
(626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing. Declares
ONLY runFullTrust. No telemetry.

One disclosure-surface change, opt-in, OFF by default: "Publish usage"
in Settings. The user may enter an endpoint URL of their own choosing;
once a day Sanduhr then posts a per-project token-count summary there (folder names only, never paths or content). Nothing is
sent until the user turns it on, sets an endpoint, and ticks at least
one Claude Code home (all unticked by default). The pasted credential
lives in Windows Credential Manager, never in a file. The in-app
explainer reads:
"Nothing about your usage comes back to us. If you want it somewhere,
send it to yourself: once a day Sanduhr can post token counts per project
name (folder names only, never paths) to an endpoint you choose." A
626 Labs dashboard preset is one option; if chosen, the record goes to
the user's own dashboard account. The privacy policy
lists the exact payload. A default install still contacts only claude.ai.

Also new: a small local snapshot (%APPDATA%\Sanduhr\snapshot.json:
percentages, reset times, plan, writer version; no keys or account
names) for the user's own Claude Code statusline, installed only on
consent from Settings, removable there. A separate developer tool (not
in this package) can ask the widget to run that publish, honored only
while it is on.

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
Updates the approved v3.3.0.0 — same Identity (626LabsLLC.SanduhrfrClaude)
and Publisher CN. No telemetry; declares ONLY runFullTrust.

One disclosure change, opt-in, OFF by default: "Publish usage". The user
may enter an endpoint URL of their own and Sanduhr posts a daily
per-project token-count summary there (folder names only, never paths or
content). Nothing is sent until the user turns it on, sets an endpoint,
and ticks a Claude Code home; the token lives in Credential Manager. A
default install still contacts only claude.ai. Also new: a local snapshot
file the user's Claude Code statusline reads (percentages, no keys),
installed on consent from Settings.

Unchanged: tool-strip navigation with accessible names, light/dark-
legible dialogs, in-app sign-in reading only its own cookie.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.4.0.0)

- [ ] Version trio in csproj + `<Identity Version>` = `3.4.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.4.0.0.msix` built off the `v3.4.0.0` tag, unsigned
- [ ] ONLY `runFullTrust` declared; trademark disclaimer on all six surfaces (Store description,
      Copyright field, manifest Description, privacy policy, README, About box)
- [ ] **Branded tiles are the committed set** (first release shipping
      `generate-store-assets.ps1` output from the real lockup; check the Start tile and splash
      on the installed MSIX)
- [ ] **Publish usage verified on the MSIX build:** tab renders, master switch off on a fresh
      install, "Publish now" with no endpoint refuses with the remedy text, token entry masked,
      "Clear token" removes the Credential Manager entry, each home unticked by default
- [ ] **Statusline consent dialog** shows on "Install statusline…", "Remove statusline" reverts
      the `settings.json` entry and deletes `snapshot.json`; both dialogs legible on a light-mode
      host
- [ ] `docs/PRIVACY.md` carries the Publish usage rows and the second-destination section (it
      does, dated 2026-09-13); **the hosted policy at 626labs.dev/privacy.html#privacy-sanduhr
      says the same before commit** (lives in the 626labs-hub repo, not here)
- [ ] "What's new" through the Store Listing Console from
      `store-listing-console/apps/sanduhr/copy/whats-new-3.4.0.0.md` (approved 2026-09-13);
      Notes for certification pasted by hand from the block above
- [ ] `dotnet test` green on `Sanduhr.Tests` (588) and `Sanduhr.Mcp.Tests` (44) (run by path;
      the slnx does not include the MCP projects)

## Source

Supersedes [`reviewer-letter-3.3.0.0.md`](./reviewer-letter-3.3.0.0.md). Public listing copy for
this version is written from `store-listing-console/apps/sanduhr/copy/` and archived here as
`listing-copy-3.4.0.md` after certification (runbook Phase 7, step 8). GitHub release notes:
[`release-notes-3.4.0.0.md`](./release-notes-3.4.0.0.md).
