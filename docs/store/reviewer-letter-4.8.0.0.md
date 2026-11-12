# Notes for certification — reviewer letter (v4.8.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes for certification.
> This field never goes through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: a feature release with no new destination and no new capability. The one new disclosure is a local read:
> Sanduhr reads the plan percents Codex writes into its own session logs on this PC. It adds two local files and one
> MIT-licensed decompression library. The manifest changes only its version.

---

```
Hello reviewer,

This updates v4.7.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

New in 4.8.0.0: the widget can show Codex's 5-hour and weekly plan limits
beside Claude's. When the user turns Codex on (Set up Sanduhr, or
Settings > Usage > Sources), Sanduhr reads the session logs Codex already
writes on this PC (%USERPROFILE%\.codex\sessions, or the folder the
CODEX_HOME setting names) for the percents and reset times only. It never
opens Codex's sign-in file, keeps no prompt or reply, needs no sign-in and
makes no network request. The numbers stay in two local files; the
privacy policy gains a paragraph and two rows for them.

To review: Set up Sanduhr > tick Codex. Without Codex installed, the
Codex tile says so; with it, run one Codex prompt and the widget shows
Codex's limits under Claude's.

No new network destination. One new library: ZstdSharp.Port (MIT), to
read Codex's compressed logs.

Everything from 4.0 is preserved (10.1.4.4): the trademark disclaimer,
burn-rate projection and pacing, the Privacy page, tool-strip navigation
with accessible names, and dialogs legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, and "Codex" is
a trademark of OpenAI, used nominatively. Sanduhr für Claude is an
independent third-party tool, not affiliated with or endorsed by
Anthropic PBC or OpenAI.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v4.8.0.0)

- [ ] First line names the live version (4.7.0.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.8.0.0`; `Sanduhr.Mcp.csproj` at 4.8.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only; `git diff v4.7.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `windows-dotnet/dist/Sanduhr-Store-v4.8.0.0.msix` from the `v4.8.0` release draft
- [ ] The hosted privacy policy (626labs.dev/privacy.html#privacy-sanduhr) carries the Windows Codex paragraph and both rows from `docs/PRIVACY.md` before commit
- [ ] The Store listing copy in the Listing Console that names Codex carries the Codex trademark line
- [ ] The live check of the Codex path on a dev build passed (PR #494's checklist)
- [ ] `dotnet test windows-dotnet` green
- [ ] The paste block is under Partner Center's 2000-character cap for Notes for certification

## Source

Follows [`reviewer-letter-4.7.0.0.md`](./reviewer-letter-4.7.0.0.md).
