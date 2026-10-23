# Notes for certification — reviewer letter (v3.8.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; this block is about 1,830 characters, the fallback about 710). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: the live version is 3.7.1.0. 3.7.2 shipped on GitHub only (fixes), so this letter covers
> both; the 3.7.2 fixes add nothing to the disclosure surface. 3.8.0 adds **one outbound destination**
> and no capability: the manifest changes only its version and still declares `runFullTrust` only.
> The new destination is GitHub releases of `626Labs-LLC/sanduhr-widgets` (`catalog.json`, its
> `.sig` / `.sig2` and pack zips, with GitHub's asset hosts `objects.githubusercontent.com` and
> `release-assets.githubusercontent.com` behind its redirects), reached only when the user opens
> Settings ▸ Widgets, presses Check the catalog again, or presses Install / Update. Everything else is
> local: installed packs in `%LOCALAPPDATA%\Sanduhr\widgets\` (package storage on the Store build),
> `companion.json`, and the `widget-request.json` / `widget-result.json` handoff in
> `%USERPROFILE%\.sanduhr`. Restart to update uses `StoreContext`, which needs no capability. Model
> names appear in no keyword (the 3.5.0 failure).

---

```
Hello reviewer,

This updates v3.7.1.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

One new network destination: Settings > Widgets fetches a widget catalog
and pack zips from github.com/626Labs-LLC/sanduhr-widgets releases (and
GitHub's asset hosts it redirects to), only when the user opens that page
or presses Install or Update. Nothing polls; no account or identifier is
sent. Packs are data only (JSON, PNG, fonts; no code). The catalog's
ECDSA P-256 signature is verified against a key pinned in the app before
it is parsed, each zip's size and SHA-256 must match, and every pack is
re-validated locally before it is installed.

New since 3.7.1, all local:
- Claude Code, through Sanduhr's local MCP server, can propose a widget
  (pixel sprite, palette or effects, as data). It previews with Apply
  and Dismiss and installs only on Apply; the direct-change switch
  never covers widgets. A user's own PNG art stays on the PC.
- A Desk companion that grows with coding days, read from local files.
- Restart to update: the Store update installs through Windows' own
  dialog, only after the user clicks.

Preserved (10.1.4.4): (a) trademark disclaimer on every surface naming
Claude, specific copyright entity, no model names in keywords; (b)
burn-rate projection and pacing, focus hourglass, cooldown game, themes,
Mica, Credential Manager storage; (c) tool-strip navigation with
accessible names, shortcuts in Settings > Help, native close, dialogs
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
Updates v3.7.1.0, same Identity and Publisher CN. Only runFullTrust is
declared; no telemetry.

One new destination: Settings > Widgets fetches a signed widget catalog
and pack zips from github.com/626Labs-LLC/sanduhr-widgets releases, only
when the user opens that page or installs. Packs are data only (JSON,
PNG, fonts; no code), signature-checked (ECDSA P-256, pinned key) and
re-validated locally. Widgets Claude Code proposes install only on
Apply. Updates install only after the user clicks Restart to update.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.8.0.0)

- [ ] First line names the live version (3.7.1.0); 3.7.2 was GitHub-only and is covered here
- [ ] Version trio in csproj + `<Identity Version>` = `3.8.0.0`; `Sanduhr.Mcp.csproj` at 3.8.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only (`PackageManifestTests` green); `git diff v3.7.1 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] What's New cards tagged `3.8.0` show after the update: a 3.7.1 install sees the seven 3.8.0 cards (eight if meter styles lands)
- [ ] `dist/Sanduhr-Store-v3.8.0.0.msix` built off the `v3.8.0` tag, unsigned; `mcp\sanduhr-mcp.exe` trimmed; `scripts/smoke-mcp.ps1` passes and lists `propose_widget`
- [ ] **Everything off or empty on a fresh install:** no widget installed, no companion on the Desk, Gradient bars off, no Show it as name, direct Desk changes off
- [ ] **No network before the user asks:** a fresh install with Settings ▸ Widgets never opened makes no request to github.com (check `sanduhr.log` and a network capture); opening the page fetches the catalog and nothing else until Install
- [ ] **A tampered catalog installs nothing:** the page says "The widget catalog couldn't be verified" (the `WidgetCatalog` tests cover the signature, rollback, size and SHA-256 cases)
- [ ] **A proposed widget changes nothing until Apply;** Dismiss removes the staged copy in `widgets\.preview\`
- [ ] **Restart to update offers, never acts:** nothing restarts without the click (Phase 7 step 9 proves the real Store path on the first 3.8.x)
- [ ] Reset Sanduhr… removes `widgets\` (with `companion.json`) and `widget-request.json` / `widget-result.json`
- [ ] Store keywords: no model names (Fable, Opus, Sonnet, Haiku), the 3.5.0 failure; first-party packs carry no Anthropic marks (the validator's trademark rule)
- [ ] Trademark disclaimer on all six surfaces plus What's New, the tour and the Desk pages
- [ ] `docs/PRIVACY.md` rows (Widget packs, The Desk companion, Widgets from Claude, Your own art, the GitHub network section, Show it as) and the hosted policy at 626labs.dev/privacy.html#privacy-sanduhr match **before commit**: open the hub issue from [`hub-privacy-3.8.0.md`](./hub-privacy-3.8.0.md), then curl-check the live page for `sanduhr-widgets`, `widget-request.json` and `companion.json`
- [ ] Store "What's new" keeps the line *"If the update won't install (error 0x80073D02), restart Windows and try again."* (the 3.7.1 to 3.8 hop runs 3.7.1's Quit and update)
- [ ] "What's new" through the Store Listing Console from `store-listing-console/apps/sanduhr/copy/whats-new-3.8.0.0.md`; Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-3.7.1.0.md`](./reviewer-letter-3.7.1.0.md) (live). GitHub release notes:
[`release-notes-3.8.0.0.md`](./release-notes-3.8.0.0.md); 3.7.2's, GitHub only:
[`release-notes-3.7.2.0.md`](./release-notes-3.7.2.0.md).
