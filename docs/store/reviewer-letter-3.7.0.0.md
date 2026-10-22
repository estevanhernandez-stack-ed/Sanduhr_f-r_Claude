# Notes for certification — reviewer letter (v3.7.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; this block is about 1,700 characters, the fallback about 730). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: the live version is 3.5.0.0 (certified 2026-10-21). 3.6.0 shipped on GitHub only, so this
> letter covers both releases; the 3.6.0 changes (watchers, suggested Desk messages, the plate, Arrange
> hide/show, the statusline folder name) are inside "local files" below. 3.7.0 adds **no capability and
> no outbound destination**; the manifest changes only its version and still declares `runFullTrust` only.
> The disclosure surface gains local files only: the Desk state, a design request and result, a scaled
> copy of the wallpaper, song-look files and the watcher and Desk-message files, all in
> `%USERPROFILE%\.sanduhr`, plus saved designs and saved song looks in the package's own storage. A
> saved song look holds the song's title and artist on the PC; nothing is sent. Font download links are
> shown to the user and never fetched; the Launcher opens an app or link only on the user's click.
> Every change Claude proposes waits for the user unless a switch, off by default, is turned on. Model
> names appear in no keyword (the 3.5.0 failure).

---

```
Hello reviewer,

This updates v3.5.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry, no new network
destination (font links are shown, never fetched).

New since 3.5.0, all local:
- Claude Code, through Sanduhr's local MCP server, can read the Desk and
  a wallpaper summary and propose a look. It draws as a preview with
  Apply and Dismiss; nothing is saved until the user applies, unless
  "Let Claude change the Desk directly" (off by default) is on. Files
  pass in %USERPROFILE%\.sanduhr (desk-state.json, design request and
  result, a scaled wallpaper-preview.png, now-playing look files,
  watcher and Desk-message files). Saved designs and song looks stay on
  the PC.
- A Launcher piece: up to 8 buttons opening Start-menu apps or https
  links, only on the user's click; no command lines.
- Desk pieces per monitor, a look per song (off by default), OLED care
  (off by default).

Preserved (10.1.4.4): (a) trademark disclaimer on every surface naming
Claude, specific copyright entity, no model names in keywords; (b)
burn-rate projection and pacing, focus hourglass, cooldown game, themes,
Mica, Credential Manager storage; (c) tool-strip navigation with
accessible names (Settings search, Quit and Designs list exposed to
screen readers), shortcuts in Settings > Help, native close, dialogs
legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## If the field is shorter (sub-1000 char fallback)

```
Updates v3.5.0.0, same Identity and Publisher CN. Only runFullTrust is
declared; no new network destination, no telemetry.

New, all local: through Sanduhr's MCP server, Claude Code can propose a
Desk look; it previews with Apply/Dismiss and changes nothing until the
user applies (a direct-apply switch is off by default). Files stay in
%USERPROFILE%\.sanduhr. Also a Launcher (Start-menu apps and https
links, opened only on click; no command lines), pieces per monitor, a
look per song and OLED care (both off by default).

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.7.0.0)

- [ ] First line names the live version (3.5.0.0); 3.6.0 was GitHub-only and is covered here
- [ ] Version trio in csproj + `<Identity Version>` = `3.7.0.0`; `Sanduhr.Mcp.csproj` at 3.7.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only (`PackageManifestTests` green)
- [ ] What's New cards tagged `3.7.0` show after the update; a 3.5.0 install sees six 3.7.0 cards and two of 3.6.0 (cap 8)
- [ ] `dist/Sanduhr-Store-v3.7.0.0.msix` built off the `v3.7.0` tag, unsigned; `mcp\sanduhr-mcp.exe` trimmed; `scripts/smoke-mcp.ps1` passes and lists `get_desk`, `propose_desk` and `propose_now_playing_looks`
- [ ] **Everything off by default on a fresh install:** direct Desk changes, direct song styling, Style what's playing, OLED care, a Launcher with no buttons
- [ ] **A proposal changes nothing until Apply** (or Save for song looks); Dismiss restores the Desk at once
- [ ] **No new outbound call:** a design naming a missing font shows its link and fetches nothing; Launcher buttons open only on click and only what the Start menu lists
- [ ] Reset Sanduhr… removes `designs\`, `launcher\` and `now-playing-looks.json` with the settings
- [ ] Store keywords: no model names (Fable, Opus, Sonnet, Haiku), the 3.5.0 failure
- [ ] Trademark disclaimer on all six surfaces plus What's New, the tour and the Desk pages
- [ ] `docs/PRIVACY.md` rows (desk-state.json, wallpaper-preview.png, design request/result, saved designs, saved song looks, song look request/result, `now-playing-look.json`, the Launcher); the hosted policy at 626labs.dev/privacy.html#privacy-sanduhr matches **before commit** (open a hub issue after #258 and curl-check the live page for the new file names)
- [ ] "What's new" through the Store Listing Console from `store-listing-console/apps/sanduhr/copy/whats-new-3.7.0.0.md`; Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-3.6.0.0.md`](./reviewer-letter-3.6.0.0.md) (GitHub only) and
[`reviewer-letter-3.5.0.0.md`](./reviewer-letter-3.5.0.0.md) (live). GitHub release notes:
[`release-notes-3.7.0.0.md`](./release-notes-3.7.0.0.md).
