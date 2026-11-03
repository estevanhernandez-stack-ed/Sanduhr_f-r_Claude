# Notes for certification — reviewer letter (v4.6.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes for certification.
> This field never goes through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: a feature release with no new destination and no new capability. What changes on the disclosure surface is
> local and needs saying up front, because a reviewer may see it in a process monitor: the app now installs a low-level
> mouse hook (`WH_MOUSE_LL`) and puts child windows inside Explorer's desktop window, to draw the Desk below the desktop
> icons and keep it clickable there. Both are standard Win32 for a desktop app and need no capability beyond
> `runFullTrust`. The manifest changes only its version.

---

```
Hello reviewer,

This updates v4.5.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

New in 4.6.0.0: the optional Desk (clock, meters and pieces the user
places) draws below the desktop icons. Two things you may see in a
process monitor:

1. Child windows inside Explorer's desktop window (Progman's WorkerW),
   drawn with DirectComposition. On by default; Settings > Desk Look >
   "Keep the Desk under desktop icons" turns it off.

2. A low-level mouse hook (WH_MOUSE_LL), so the Desk stays clickable
   under the icons: a right-click on a widget opens the Desk menu, a
   left-click on now playing plays or pauses. It acts only on a left or
   right press over a drawn part of a Desk widget; everything else
   passes untouched. It records nothing, never reads the keyboard and
   sends nothing. It reads where desktop icons sit (read-only) and which
   window is under the pointer, never its title or content. It exists
   only while the Desk is shown under the icons.

To review: Settings > Desk > tick "Desk", then right-click the clock on
the desktop.

No new network destination. The privacy policy gains a row for this.

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

## Pre-submission sanity check (v4.6.0.0)

- [ ] First line names the live version (4.5.0.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.6.0.0`; `Sanduhr.Mcp.csproj` at 4.6.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only; `git diff v4.5.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `windows-dotnet/dist/Sanduhr-Store-v4.6.0.0.msix` from the `v4.6.0` release draft
- [ ] The hosted privacy policy (626labs.dev/privacy.html#privacy-sanduhr) carries the "Desk under your desktop icons" row from `docs/PRIVACY.md` before commit
- [ ] Store "What's new" keeps the 0x80073D02 line until the restart-to-update check passes (dashboard a36UEAoiqo1OKKBCDyCy)
- [ ] `dotnet test windows-dotnet` green
- [ ] The paste block is under Partner Center's 2000-character cap for Notes for certification

## Source

Follows [`reviewer-letter-4.5.0.0.md`](./reviewer-letter-4.5.0.0.md).
