# Notes for certification — reviewer letter (v3.9.1.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; this block measures 1,224 characters, the fallback 518; 1,250 and 529 if the field counts a line break as two). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: 3.9.1 is a patch that fixes widget packs and adds **no outbound destination and no
> capability**: the manifest changes only its version and still declares `runFullTrust` only.
> Nothing new is read or sent. Installed theme, palette and font packs now apply: themes join the
> theme list, palettes the Desk ink presets, fonts the Desk font lists (a font loads from a copy
> under the widgets folder). No new setting and no What's New card.
> Confirm which version is live before pasting: the first line below names 3.9.0.0.

---

```
Hello reviewer,

This updates v3.9.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry, no new network
destination and no new capability.

Fixed in 3.9.1.0: the themes, palettes and fonts of installed widget
packs now apply. They installed in 3.8 but nothing used them; now themes
join the theme list, palettes the Desk ink presets and fonts the Desk
font lists. Sanduhr still reads only the pack files already on the user's
PC, loads a font from a copy in its own widgets folder, and sends nothing
new anywhere.

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
Updates v3.9.0.0, same Identity and Publisher CN. Only runFullTrust is
declared; no telemetry, no new network destination, no new capability.

Fixed: themes, palettes and fonts of installed widget packs now apply
(theme list, Desk ink presets, Desk fonts). Only pack files already on
the user's PC are read; nothing new is sent.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.9.1.0)

- [ ] First line names the live version (3.9.0.0); confirm in Partner Center before pasting
- [ ] Version trio in csproj + `<Identity Version>` = `3.9.1.0`; `Sanduhr.Mcp.csproj` at 3.9.1
- [ ] `Package.appxmanifest` declares `runFullTrust` only (`PackageManifestTests` green); `git diff v3.9.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `dist/Sanduhr-Store-v3.9.1.0.msix` built off the `v3.9.1` tag, unsigned; `mcp\sanduhr-mcp.exe` trimmed; `scripts/smoke-mcp.ps1` passes
- [ ] **Trust boundary unchanged:** `TrustBoundaryTests` and `DockerPipeBoundaryTests` green; `Sanduhr.Mcp.csproj`'s link list is as in 3.9.0 (the pack code is in Core files it does not link)
- [ ] **Packs apply:** install OLED Neon from Settings ▸ Widgets; its themes are in Settings ▸ Themes, its palettes in Settings ▸ Desk ▸ Ink, and a font pack's family in the Desk font list after Settings reopens. Uninstalling the theme in use switches to Obsidian at once
- [ ] Reset Sanduhr… needs nothing new: pack fonts copy under `widgets\.fonts`, inside the widgets folder Reset already lists
- [ ] Store keywords: no model names (Fable, Opus, Sonnet, Haiku), the 3.5.0 failure
- [ ] Trademark disclaimer on all six surfaces plus What's New, the tour and the Desk pages
- [ ] No hub privacy change: nothing new is read or sent, so `docs/PRIVACY.md` and the hosted policy stand as for 3.9.0
- [ ] Store "What's new" keeps the line *"If the update won't install (error 0x80073D02), restart Windows and try again."*: drop it in the first release after the Phase 7 step 9 restart-to-update check passes on a real Store update
- [ ] "What's new" through the Store Listing Console from `store-listing-console/apps/sanduhr/copy/whats-new-3.9.1.0.md`; Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-3.9.0.0.md`](./reviewer-letter-3.9.0.0.md). Fix: `CHANGELOG.md`, v3.9.1.
