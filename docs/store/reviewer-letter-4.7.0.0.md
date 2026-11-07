# Notes for certification — reviewer letter (v4.7.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes for certification.
> This field never goes through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: a feature release with no new destination, no new capability and nothing new in a process monitor. The one
> new feature reads a local file the user picks with the standard file dialog. The manifest changes only its version.

---

```
Hello reviewer,

This updates v4.6.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

New in 4.7.0.0: Settings > Mods & Widgets > "Install widget from file..."
installs a widget pack (a .zip of JSON, PNG art and fonts, no code) that the
user picks with the standard Windows file dialog. The zip is read once,
checked with the same rules as a pack from the signed catalog, and a
pack that passes is copied into the app's own widgets folder. It is
never uploaded and makes no network request. The rest of 4.7.0.0 is
fixes: lower CPU use and the Desk following Windows' animation setting.

To review: Settings > Mods & Widgets > Install widget from file...,
then pick any pack zip; a non-pack zip is refused with a reason.

No new network destination. The privacy policy gains a line for this.

Everything from 4.0 is preserved (10.1.4.4): the trademark disclaimer,
burn-rate projection and pacing, the Privacy page, tool-strip navigation
with accessible names, and dialogs legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v4.7.0.0)

- [ ] First line names the live version (4.6.0.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.7.0.0`; `Sanduhr.Mcp.csproj` at 4.7.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only; `git diff v4.6.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `windows-dotnet/dist/Sanduhr-Store-v4.7.0.0.msix` from the `v4.7.0` release draft
- [ ] The hosted privacy policy (626labs.dev/privacy.html#privacy-sanduhr) carries the Windows "Install widget from file" line from `docs/PRIVACY.md` before commit
- [ ] Store "What's new" keeps the 0x80073D02 line until the restart-to-update check passes (dashboard a36UEAoiqo1OKKBCDyCy)
- [ ] `dotnet test windows-dotnet` green
- [ ] The paste block is under Partner Center's 2000-character cap for Notes for certification

## Source

Follows [`reviewer-letter-4.6.0.0.md`](./reviewer-letter-4.6.0.0.md).
