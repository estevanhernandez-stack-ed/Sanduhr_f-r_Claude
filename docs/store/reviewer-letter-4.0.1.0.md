# Notes for certification — reviewer letter (v4.0.1.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes for certification.
> This field never goes through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: a fix release. One Settings view changes (the Desk layout map draws every monitor). No capability, no
> destination, no new data; the manifest changes only its version and still declares `runFullTrust` only.

---

```
Hello reviewer,

This updates v4.0.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

4.0.1.0 is a fix release. Settings > Desk > Layout map now shows every
connected monitor instead of the main one only. No new network
destination, no new capability, no new data stored.

Everything from 4.0.0.0 is preserved (10.1.4.4): the trademark disclaimer
on every surface naming Claude, burn-rate projection and pacing, the
Privacy page listing every destination, tool-strip navigation with
accessible names, and dialogs legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v4.0.1.0)

- [ ] First line names the live version (4.0.0.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.0.1.0`; `Sanduhr.Mcp.csproj` at 4.0.1
- [ ] `Package.appxmanifest` declares `runFullTrust` only; `git diff v4.0.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `windows-dotnet/dist/Sanduhr-Store-v4.0.1.0.msix` from the `v4.0.1` release draft
- [ ] Store "What's new" keeps the 0x80073D02 line until the restart-to-update check passes (dashboard a36UEAoiqo1OKKBCDyCy)
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-4.0.0.0.md`](./reviewer-letter-4.0.0.0.md).
