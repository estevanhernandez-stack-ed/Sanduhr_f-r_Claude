# Notes for certification — reviewer letter (v3.4.3.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; front-loaded per playbook gotcha #8; this block is about
> 1,550 characters). This field never goes through the Store Listing Console API, so it is
> confirmed in Partner Center or not at all.
>
> Framing: v3.4.2.0 is the approved current version. v3.4.3 is a Store-only fix with **one new
> restricted capability, `unvirtualizedResources`**, and nothing else on the disclosure surface:
> no new outbound destination, no new at-rest data. Microsoft Learn describes this capability as
> designed for games and says it is not intended for other scenarios because of clean uninstall,
> so the letter leads with why the app needs it and that on Windows 11 it is scoped to the two
> Sanduhr folders only. It carries the 10.1.4.4 (a)(b)(c) statements and the trademark
> disclaimer forward.

---

```
Hello reviewer,

This updates the approved v3.4.2.0: same Identity
(626LabsLLC.SanduhrfrClaude), Publisher CN and listing. No telemetry, no
new outbound destination, no new at-rest data.

One new restricted capability: unvirtualizedResources. Sanduhr ships a
local, read-only MCP server (sanduhr-mcp) and a Claude Code statusline
script. Both run outside the package, started by Claude Code, and read
files the widget writes under %APPDATA%\Sanduhr (usage snapshot, history,
settings, the server binary). With AppData writes virtualized those files
land in the package's private LocalCache and the tools cannot find them.
On Windows 11 the manifest unvirtualizes only %APPDATA%\Sanduhr and
%LOCALAPPDATA%\Sanduhr (ExcludedDirectories); Windows 10 uses
FileSystemWriteVirtualization=disabled. Registry virtualization stays on.
On first launch the app copies data an earlier version left in LocalCache
to those folders once. Minimum OS is now Windows 10 1903.

Preserved (10.1.4.4): (a) trademark disclaimer on every surface naming
Claude, specific copyright entity; (b) burn-rate projection and pacing,
focus hourglass, cooldown game, themes, Mica, Credential Manager storage;
(c) tool-strip navigation with accessible names, shortcuts in
Settings > Help, native close, dialogs legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## If the field is shorter (sub-1000 char fallback)

```
Updates the approved v3.4.2.0, same Identity and Publisher CN. No
telemetry, no new outbound destination, no new at-rest data.

New: unvirtualizedResources. A local MCP server and a Claude Code
statusline script, both outside the package, read files the widget writes
under %APPDATA%\Sanduhr; virtualized, those land in LocalCache where they
cannot be found. On Windows 11 only %APPDATA%\Sanduhr and
%LOCALAPPDATA%\Sanduhr are unvirtualized; registry virtualization stays
on. Minimum OS is now Windows 10 1903.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.4.3.0)

- [ ] Version trio in csproj + `<Identity Version>` = `3.4.3.0` (4th component `.0`); `Sanduhr.Mcp.csproj` at 3.4.3
- [ ] Manifest declares `runFullTrust` and `unvirtualizedResources`, `desktop6:FileSystemWriteVirtualization`
      = `disabled`, `virtualization:ExcludedDirectories` = the two Sanduhr folders, `MinVersion` `10.0.18362.0`
      (`PackageManifestTests` green)
- [ ] `dist/Sanduhr-Store-v3.4.3.0.msix` built off the `v3.4.3` tag, unsigned; `mcp\sanduhr-mcp.exe` present in the staged payload and `scripts/smoke-mcp.ps1` passes against `dist/publish/mcp/sanduhr-mcp.exe`
- [ ] **Unvirtualized writes verified on the MSIX build:** with the MCP integration installed,
      `%APPDATA%\Sanduhr\mcp\v3.4.3-<stamp>\sanduhr-mcp.exe` and `snapshot.json` exist at the real path
      (not under `%LOCALAPPDATA%\Packages\626LabsLLC.SanduhrfrClaude_wz1chhb2h2v4a\LocalCache`), and
      Claude Code's `ping` answers from a terminal outside the package
- [ ] **Upgrade migration verified:** install the 3.4.2 Store build, change a setting and let history
      record, upgrade to 3.4.3: the setting and history are still there, `sanduhr.log` has a
      `packaged-migration done` line, `%APPDATA%\Sanduhr\.packaged-migration` says `done`
- [ ] Trademark disclaimer on all six surfaces (Store description, Copyright field, manifest
      Description, privacy policy, README, About box)
- [ ] **Support contact set to `support@626labs.dev`** on the Properties page (the API's
      `supportContact` field is obsolete, so this is hand-only and publishes with this submission)
- [ ] `docs/PRIVACY.md` updated (Store uninstall leaves both Sanduhr folders); the hosted policy at
      626labs.dev/privacy.html#privacy-sanduhr needs the same line
- [ ] "What's new" through the Store Listing Console from
      `store-listing-console/apps/sanduhr/copy/whats-new-3.4.3.0.md`;
      Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green (Sanduhr.Tests 727, Sanduhr.Mcp.Tests 87)

## Source

Supersedes [`reviewer-letter-3.4.2.0.md`](./reviewer-letter-3.4.2.0.md). GitHub release notes:
[`release-notes-3.4.3.0.md`](./release-notes-3.4.3.0.md).
