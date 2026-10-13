# Notes for certification — reviewer letter (v3.5.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; front-loaded per playbook gotcha #8; this block is about
> 1,430 characters; the fallback about 620). This field never goes through the Store Listing Console API, so it is
> confirmed in Partner Center or not at all.
>
> Framing: v3.4.3.0 is the version before this one (in certification when this letter was
> written; if it is not approved by the time 3.5.0.0 goes up, change the first line to the last
> approved version). v3.5.0 is a feature release, the Windows parity cycle, and **no new
> capability**: `Package.appxmanifest` is unchanged between the `v3.4.3` tag and this release
> except for `Identity Version` (verified against the diff). The disclosure surface gains one
> opt-in outbound destination, Open-Meteo for the Desk's weather, and nothing stored at rest
> beyond Sanduhr's own folders. The letter leads with the Desk, since it draws on the desktop,
> then names the reviewer-visible behaviours (Just the Desk hides the widget, Reset removes the
> Desk messages) and carries the 10.1.4.4 (a)(b)(c) statements and the trademark disclaimer
> forward.

---

```
Hello reviewer,

This updates v3.4.3.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. No new capabilities since 3.4.3 (verified
against the diff of Package.appxmanifest). No telemetry.

New, the Desk: Sanduhr's optional Desk draws a clock and your usage
meters on the desktop behind your windows. It is off by default; turn it
on or off in Settings > Desk. Its usage line shows the word "claude";
the Desk settings pages carry the trademark notice. Optional pieces: a
message line, now playing (Windows media controls, kept in memory),
CPU/RAM/disk, and weather from Open-Meteo for a city the user types
(only the city name and rounded coordinates are sent; no location
capability).

Choosing Just the Desk on the first-run card hides the widget; the tray
icon brings it back. The Reset "Settings" checkbox also removes the Desk
messages.

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
Updates v3.4.3.0, same Identity and Publisher CN. No new capabilities
(verified against the manifest diff). No telemetry.

New: an optional Desk draws a clock and usage meters on the desktop
behind windows; off by default, Settings > Desk. Optional weather from
Open-Meteo for a typed city (city name, rounded coordinates; no location
capability). Just the Desk on the first-run card hides the widget; the
tray icon brings it back.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.5.0.0)

- [ ] 3.4.3.0 approved and live before 3.5.0.0 is submitted (if not, fix the letter's first line)
- [ ] Version trio in csproj + `<Identity Version>` = `3.5.0.0` (4th component `.0`); `Sanduhr.Mcp.csproj` at 3.5.0
- [ ] `git diff v3.4.3 v3.5.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only `Identity Version`
      (capabilities still `runFullTrust` and `unvirtualizedResources`; `PackageManifestTests` green)
- [ ] What's New cards tagged `3.5.0` show after the update (the app reports Major.Minor.Build = 3.5.0)
- [ ] `dist/Sanduhr-Store-v3.5.0.0.msix` built off the `v3.5.0` tag, unsigned; `mcp\sanduhr-mcp.exe` and
      `mods\sanduhr-meters` present in the staged payload, `scripts/smoke-mcp.ps1` passes
- [ ] **Desk off by default on a fresh install;** Settings ▸ Desk turns it on and off; the Desk settings
      pages show the trademark notice
- [ ] **Just the Desk:** first-run card hides the widget, the tray icon brings it back
- [ ] **Reset:** the "Settings" part removes `messages.txt`
- [ ] Weather: only the typed city (geocoding) and rounded coordinates (forecast) go to Open-Meteo; the
      credit shows on the Desk; About lists Open-Meteo and GeoNames
- [ ] Trademark disclaimer on all six surfaces (Store description, Copyright field, manifest
      Description, privacy policy, README, About box) plus What's New, the tour and the Desk pages
- [ ] `docs/PRIVACY.md` Windows rows for the parity cycle read and approved; the hosted policy at
      626labs.dev/privacy.html#privacy-sanduhr matches
- [ ] "What's new" through the Store Listing Console from
      `store-listing-console/apps/sanduhr/copy/whats-new-3.5.0.0.md`;
      Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green

## Source

Supersedes [`reviewer-letter-3.4.3.0.md`](./reviewer-letter-3.4.3.0.md). GitHub release notes:
[`release-notes-3.5.0.0.md`](./release-notes-3.5.0.0.md).
