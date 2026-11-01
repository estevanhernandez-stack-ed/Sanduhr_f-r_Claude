# Notes for certification — reviewer letter (v4.5.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes for certification.
> This field never goes through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: a feature release with no new destination and no new capability. What changes on the disclosure surface is
> local: drawers move files on the user's own Desktop after a review, and one new local file (`drawers-journal.json`)
> records those moves so they can be undone. The app now runs without a Claude account, which also makes it easier
> to review. The manifest changes only its version and still declares `runFullTrust` only.

---

```
Hello reviewer,

This updates v4.0.1.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

What's new in 4.5.0.0:

1. Set up Sanduhr. A new install opens one screen of choices: a desktop
   clock, message and weather; drawers for desktop files; and AI usage
   tracking. Any mix works. With usage tracking off there is no sign-in
   and no network call to claude.ai, so you can review the app without
   a Claude account: tick only the first two options.

2. Drawers. Panels on the desktop that each show a real folder inside a
   "Drawers" folder on the user's Desktop. Built-in rules propose moving
   files there; nothing moves until the user presses Apply on a review
   that lists the files, and Undo puts them back. Turning drawers off
   moves every file back to the Desktop. Drawers only move files: they
   never delete, overwrite or rename by rule. The moves are recorded in
   %APPDATA%\Sanduhr\drawers-journal.json (paths and times) so they can be
   undone; Reset Sanduhr removes it. To keep drawers clear of desktop
   icons the app reads where Explorer's icons sit, read-only.

3. Design it with an AI chat. The app copies a prompt to the clipboard
   for any AI chat the user chooses; the user pastes the chat's answer
   back. The app itself sends nothing.

No new network destination, no new capability. Settings > Privacy lists
every destination, unchanged from 4.0.

Everything from 4.0 is preserved (10.1.4.4): the trademark disclaimer on
every surface naming Claude, burn-rate projection and pacing, the
Privacy page, tool-strip navigation with accessible names, and dialogs
legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v4.5.0.0)

- [ ] First line names the live version (4.0.1.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.5.0.0`; `Sanduhr.Mcp.csproj` at 4.5.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only; `git diff v4.0.1 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `windows-dotnet/dist/Sanduhr-Store-v4.5.0.0.msix` from the `v4.5.0` release draft
- [ ] The hosted privacy policy (626labs.dev/privacy.html#privacy-sanduhr) carries the Drawers and Set up Sanduhr rows from `docs/PRIVACY.md` before commit
- [ ] Store "What's new" keeps the 0x80073D02 line until the restart-to-update check passes (dashboard a36UEAoiqo1OKKBCDyCy)
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-4.0.1.0.md`](./reviewer-letter-4.0.1.0.md).
