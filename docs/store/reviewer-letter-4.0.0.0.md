# Notes for certification — reviewer letter (v4.0.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; this block measures 1,530 characters, the fallback 679; 1,564 and 694 if the field counts a line break as two). This field never goes
> through the Store Listing Console API, so it is confirmed in Partner Center or not at all.
>
> Framing: 4.0 rebuilds Settings (Settings v2) and adds **no capability and no new destination**: the
> manifest changes only its version and still declares `runFullTrust` only. Settings ▸ Privacy now lists
> every destination, built from one table a test keeps complete (`NetworkDisclosureTests`), and that
> made three existing items disclosed for the first time, in `docs/PRIVACY.md` and the hosted policy
> (hub PR #280, which must be live before commit):
>
> - Google's sign-in page. It was reachable in 3.9 (Continue with Google on claude.ai's sign-in page)
>   but dead-ended; 4.0 finishes it in a small window of Sanduhr's. Nothing new is kept.
> - claude.ai's routines budget (`claude.ai/v1/code/routines/run-budget`), read since June 2026 with
>   the same session and on the same host as the usage call.
> - SmartScreen, which WebView2 runs as Edge does.
> Confirm which version is live before pasting: the first line below names 3.9.1.0.

---

```
Hello reviewer,

This updates v3.9.1.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust; the
manifest changes only its version. No telemetry.

New in 4.0.0.0: Settings is rebuilt, with pages grouped by task and a
search box. Settings > Privacy lists every network destination, when it
is used, what it carries and the switch that turns it off.

No new destination. Two existing ones are now disclosed there and in
the hosted privacy policy: (1) Google's sign-in page, only if the user
chooses Continue with Google on claude.ai's sign-in page; it now
finishes in a small Sanduhr window instead of dead-ending; (2) the
Claude Code routines budget, read from claude.ai with the session it
already uses for usage.

Also new, off by default: streamer mode (Settings > Privacy), which
shows codenames in place of account, folder and project names. Its two
files stay on the PC.

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
Updates v3.9.1.0, same Identity and Publisher CN. Only runFullTrust is
declared; no telemetry.

New: Settings is rebuilt; Settings > Privacy lists every destination.
No new destination; two existing ones are now disclosed there and in
the hosted policy: Google's sign-in page (only if the user picks
Continue with Google) and the Claude Code routines budget from
claude.ai, with the same session. Streamer mode, off by default,
shows codenames in place of names; its files stay on the PC.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated
with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v4.0.0.0)

- [ ] First line names the live version (3.9.1.0); confirm in Partner Center before pasting
- [ ] Version lockstep: csproj trio + `<Identity Version>` = `4.0.0.0`; `Sanduhr.Mcp.csproj` at 4.0.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only (`PackageManifestTests` green); `git diff v3.9.1 -- windows-dotnet/src/Sanduhr.App/Package.appxmanifest` shows only the version
- [ ] `dist/Sanduhr-Store-v4.0.0.0.msix` built off the `v4.0.0` tag, unsigned; `mcp\sanduhr-mcp.exe` trimmed; `scripts/smoke-mcp.ps1` passes
- [ ] **Privacy page complete:** `NetworkDisclosureTests` green; Settings ▸ Privacy lists claude.ai usage (with the routines budget), the sign-in window (with Google), Open-Meteo, the widget catalog, updates, Publish usage and Through Claude Code
- [ ] **Hosted policy live before commit:** hub PR #280 merged, then the combined curl check against 626labs.dev/privacy.html prints **22**
- [ ] **Streamer mode off on a fresh install**
- [ ] Store keywords: no model names (Fable, Opus, Sonnet, Haiku), the 3.5.0 failure. The screenshots carry none either (store-shots folds the demo Sonnet meter away)
- [ ] Trademark disclaimer on all six surfaces plus What's New, the tour and the Desk pages
- [ ] Store "What's new" keeps the 0x80073D02 line: the restart-to-update check (runbook Phase 7 step 9) has not passed (dashboard a36UEAoiqo1OKKBCDyCy). Run it on the 3.9.1 to 4.0 update; drop the line in the next release if it passes
- [ ] Screenshots: the ten from `store-listing-console/apps/sanduhr/assets/screenshots` (console PR #49), uploaded by hand; then `captions`
- [ ] "What's new" through the Store Listing Console from `store-listing-console/apps/sanduhr/copy/whats-new-4.0.0.0.md`; Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green (Core 4679, App 1078, Mcp 299 on f00b185d)

## Source

Follows [`reviewer-letter-3.9.1.0.md`](./reviewer-letter-3.9.1.0.md). Design:
[`../settings-v2-spec.md`](../settings-v2-spec.md).
