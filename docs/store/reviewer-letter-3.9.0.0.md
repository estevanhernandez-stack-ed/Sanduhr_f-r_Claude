# Notes for certification — reviewer letter (v3.9.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; this block measures 1,463 characters, the fallback 688; 1,492 and 702 if the field counts a line break as two). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: 3.9.0 adds **no outbound destination and no capability**: the manifest changes only its
> version and still declares `runFullTrust` only. The one new surface is optional and off by default,
> Settings ▸ Desk ▸ Show my CI runners' jobs: a named-pipe client (`\\.\pipe\docker_engine`, Docker's
> local pipe) that lists containers and follows the logs of GitHub Actions runner containers on the
> user's own PC. Two read-only Docker calls and no others; `DockerPipeBoundaryTests` pins that by
> structure. Nothing is written to disk beyond the switch (`watchers_ci_runners` in `settings.json`).
> Confirm which version is live before pasting: the first line below names 3.8.0.0.

---

```
Hello reviewer,

This updates v3.8.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry, and no new network
destination.

New in 3.9.0.0: CI watchers (optional, off by default). With Settings >
Desk > "Show my CI runners' jobs" on, Sanduhr reads the log output of
GitHub Actions runner containers on the user's own PC through Docker's
local named pipe (\\.\pipe\docker_engine), to show job progress on the
Desk. No network, no credentials, no new capability: a named-pipe client
runs under runFullTrust, which the manifest already declares. Sanduhr
makes two read-only Docker calls (list containers, follow a runner's log)
and no others. Testing without Docker installed: the switch's status line
reads "Docker isn't running." and nothing else changes.

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
Updates v3.8.0.0, same Identity and Publisher CN. Only runFullTrust is
declared; no telemetry, no new network destination.

New: optional, off by default, Settings > Desk > "Show my CI runners'
jobs". Reads the log output of GitHub Actions runner containers on the
user's own PC through Docker's local pipe, to show job progress. No
network, no credentials; two read-only Docker calls (list containers,
follow a runner's log). Without Docker installed, its status line reads
"Docker isn't running."

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.9.0.0)

- [ ] First line names the live version (3.8.0.0); confirm in Partner Center before pasting
- [ ] Version trio in csproj + `<Identity Version>` = `3.9.0.0`; `Sanduhr.Mcp.csproj` at 3.9.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only (`PackageManifestTests` green); `git diff v3.8.0 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `dist/Sanduhr-Store-v3.9.0.0.msix` built off the `v3.9.0` tag, unsigned; `mcp\sanduhr-mcp.exe` trimmed; `scripts/smoke-mcp.ps1` passes
- [ ] **Off on a fresh install:** Show my CI runners' jobs is off, and with it off Sanduhr opens no connection to Docker's pipe
- [ ] **Two Docker calls only:** `DockerPipeBoundaryTests` green (the only `HttpClient` call is `GetAsync(PathFor(...))`; the pipe name and Docker port tokens appear nowhere outside `DockerPipe.cs`, so the pipe name appears only there)
- [ ] **Without Docker installed:** switch on, the status line reads "Docker isn't running." and nothing else changes, no error card
- [ ] **Demo mode hides runner cards**
- [ ] Reset Sanduhr… needs nothing new: the only state is the `watchers_ci_runners` boolean in `settings.json`
- [ ] Store keywords: no model names (Fable, Opus, Sonnet, Haiku), the 3.5.0 failure
- [ ] Trademark disclaimer on all six surfaces plus What's New, the tour and the Desk pages
- [ ] `docs/PRIVACY.md` row (CI watchers) and the hosted policy at 626labs.dev/privacy.html#privacy-sanduhr match **before commit**: open a hub issue for the CI watchers row, then curl-check the live page for `watchers_ci_runners` and `docker_engine`
- [ ] Store "What's new" keeps the line *"If the update won't install (error 0x80073D02), restart Windows and try again."* only if the update path still needs it; otherwise drop it
- [ ] "What's new" through the Store Listing Console from `store-listing-console/apps/sanduhr/copy/whats-new-3.9.0.0.md`; Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-3.8.0.0.md`](./reviewer-letter-3.8.0.0.md). Design:
[`../ci-watchers-3.9-spec.md`](../ci-watchers-3.9-spec.md).
