# Notes for certification — reviewer letter (v4.9.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes for certification.
> This field never goes through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: a feature release with no new destination, no new capability and no new library. The one new disclosure is a
> local read: Sanduhr counts tokens from Codex's and the Antigravity CLI's own logs on this PC and keeps daily counts in
> its existing usage vault. The manifest changes only its version.

---

```
Hello reviewer,

This updates v4.8.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

New in 4.9.0.0: Sanduhr's local token counts cover Codex and the
Antigravity CLI beside Claude Code. Sanduhr reads the logs those tools
already write on this PC (%USERPROFILE%\.codex\sessions and
%USERPROFILE%\.gemini\antigravity-cli) for timestamps, project folder
names, model names and token counts only. It never keeps a prompt or a
reply, needs no sign-in and makes no network request. Daily counts go
into the existing local usage vault; the privacy policy gains a row.
Codex follows its existing switch; Antigravity has its own switch, off
by default.

To review: Settings > Usage > Sources > turn on Codex or Antigravity.
With either tool installed and used once, the widget footer shows its
new tokens and Settings > Claude Usage > Overview lists its projects.

No new network destination and no new library.

Everything from 4.0 is preserved (10.1.4.4): the trademark disclaimer,
burn-rate projection and pacing, the Privacy page, tool-strip navigation
with accessible names, and dialogs legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, "Codex" is a
trademark of OpenAI, and "Antigravity" is a trademark of Google LLC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC, OpenAI or Google LLC.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v4.9.0.0)

- [ ] First line names the live version (4.8.0.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.9.0.0`; `Sanduhr.Mcp.csproj` at 4.9.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only; `git diff v4.8.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `windows-dotnet/dist/Sanduhr-Store-v4.9.0.0.msix` from the `v4.9.0` release draft
- [ ] The hosted privacy policy (626labs.dev/privacy.html#privacy-sanduhr) carries the Codex and Antigravity activity paragraph (`docs/store/hub-privacy-4.9.0.md`) before commit
- [ ] Store listing copy that names Codex or Antigravity carries their trademark lines
- [ ] The live smoke of spec 3 on a dev build passed (PR #514, 2026-11-17)
- [ ] `dotnet test windows-dotnet` green
- [ ] The paste block is under Partner Center's 2000-character cap for Notes for certification

## Source

Follows [`reviewer-letter-4.8.0.0.md`](./reviewer-letter-4.8.0.0.md).
