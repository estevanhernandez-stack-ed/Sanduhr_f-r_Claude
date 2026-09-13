# Notes for certification — reviewer letter (v3.4.1.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; front-loaded per playbook gotcha #8). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: v3.4.0.0 is the approved current version. v3.4.1 has **one disclosure-surface
> change**: the package now carries a second executable, `mcp\sanduhr-mcp.exe`, a local helper
> for the user's own Claude Code that the app installs only on consent from Settings. It makes no
> network calls and holds no credentials. No new outbound destination; a default install still
> contacts only claude.ai. The letter leads with it, then the theme features, then carries the
> 10.1.4.4 (a)(b)(c) statements and the trademark disclaimer forward.

---

```
Hello reviewer,

This updates the approved v3.4.0.0 — same Identity
(626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing. Declares
ONLY runFullTrust. No telemetry. No new outbound destination: a default
install still contacts only claude.ai.

One disclosure-surface change: the package carries a second executable,
mcp\sanduhr-mcp.exe. It is a local helper for the user's own Claude Code
(an MCP server over stdio). The app never launches it. The user can
install it from Settings > Claude Usage > "Install MCP server": a consent
dialog names every file it touches, then the app copies the helper to
%APPDATA%\Sanduhr\mcp\, writes a launcher, and adds one entry to the
user's Claude Code configuration (with a timestamped backup beside it).
Claude Code then starts the helper as the user. The helper makes no
network calls, holds no credentials, and reads only local files: the
usage snapshot, and the user's own Claude Code session logs for the homes
the user ticked (all unticked by default). "Remove MCP server" reverts
the entry and deletes the files.

Also new: Claude Code can hand the app a color theme through that helper;
the app checks it, saves it as a file under %APPDATA%\Sanduhr\themes\, and
applies it, with the previous theme one click away. Settings > Themes
gains a token editor with the widget as live preview. Nothing in either
leaves the machine.

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
Updates the approved v3.4.0.0 — same Identity (626LabsLLC.SanduhrfrClaude)
and Publisher CN. No telemetry; declares ONLY runFullTrust. No new
outbound destination; a default install contacts only claude.ai.

One disclosure change: the package carries a second executable,
mcp\sanduhr-mcp.exe, a local helper for the user's own Claude Code. The
app never launches it; the user installs it from Settings on consent
(files copied under %APPDATA%\Sanduhr, one entry added to the user's
Claude Code config with a backup) and can remove it there. It makes no
network calls, holds no credentials, and reads only local files. Claude
Code can also hand the app a color theme through it, saved as a file
under the app's themes folder.

Unchanged: tool-strip navigation with accessible names, light/dark-
legible dialogs, in-app sign-in reading only its own cookie.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.4.1.0)

- [ ] Version trio in csproj + `<Identity Version>` = `3.4.1.0` (4th component `.0`); `Sanduhr.Mcp.csproj` at 3.4.1
- [ ] `dist/Sanduhr-Store-v3.4.1.0.msix` built off the `v3.4.1` tag, unsigned; `mcp\sanduhr-mcp.exe` present in the staged payload and `scripts/smoke-mcp.ps1` passes against `dist/publish/mcp/sanduhr-mcp.exe` (seven tools; a broken palette is refused without writing a request)
- [ ] ONLY `runFullTrust` declared; trademark disclaimer on all six surfaces (Store description,
      Copyright field, manifest Description, privacy policy, README, About box)
- [ ] **MCP install verified on the MSIX build:** "Install MCP server…" shows the consent dialog (home picker, per-home read checkboxes all off, the details line naming the files and the 3.4.0 floor), the launcher lands under `%APPDATA%\Sanduhr\bin\`, the entry lands in the chosen home's `.claude.json` (the profile-root file for the default home), "Remove MCP server" reverts it; dialog legible on a light-mode host
- [ ] **Theme Studio verified:** Studio toggle, a valid edit re-tints live, a bad hex reads red with its finding, Revert restores the saved theme, Save & apply writes `themes\<key>.json`
- [ ] `docs/PRIVACY.md` carries the MCP registration row and the vault note (dated 2026-09-13); **the hosted policy at 626labs.dev/privacy.html#privacy-sanduhr says the same before commit** (lives in the 626labs-hub repo, not here)
- [ ] "What's new" through the Store Listing Console from
      `store-listing-console/apps/sanduhr/copy/whats-new-3.4.1.0.md`;
      Notes for certification pasted by hand from the block above
- [ ] `dotnet test Sanduhr.slnx` green (Sanduhr.Tests 690, Sanduhr.Mcp.Tests 87; the MCP projects are in the solution since 3.4.1)

## Source

Supersedes [`reviewer-letter-3.4.0.0.md`](./reviewer-letter-3.4.0.0.md). Public listing copy for
this version is written from `store-listing-console/apps/sanduhr/copy/` and archived here as
`listing-copy-3.4.1.md` after certification (runbook Phase 7, step 8). GitHub release notes:
[`release-notes-3.4.1.0.md`](./release-notes-3.4.1.0.md).
