# Notes for certification — reviewer letter (v3.6.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap). This field never goes through the Store Listing Console API,
> so it is confirmed in Partner Center or not at all.
>
> Framing: the live version is 3.5.0.0 if its resubmission (keyword "Fable 5" removed, 2026-10-21)
> certifies; otherwise 3.4.2.0. **Fix the first line to whichever is live before pasting.** 3.6.0
> adds no capability, no outbound destination and no data leaving the PC. The disclosure surface
> gains local files only: requests between the MCP server and the widget in
> `%USERPROFILE%\.sanduhr`. Every new feature is off by default or waits for the user. Model names
> appear in no keyword (the 3.5.0 failure).

---

```
Hello reviewer,

This updates v3.5.0.0: same Identity (626LabsLLC.SanduhrfrClaude),
Publisher CN and listing. The only capability is still runFullTrust;
the manifest is unchanged except the version. No telemetry, and no new
network destination.

New in 3.6.0, all local:
- Watchers: with the user's switch on (Settings > Desk, "Let agents
  show watchers", off by default), Claude Code can show a progress card
  on the optional Desk through Sanduhr's local MCP server. Cards live in
  memory; requests pass as small files in %USERPROFILE%\.sanduhr.
- Suggested Desk messages: Claude Code may suggest lines; they wait in
  Settings > Message until the user chooses Add, Review or Dismiss.
- A plate behind the Desk text, hide/show while arranging the Desk, a
  Desk card in the welcome tour, and Quit at the foot of the Settings
  sidebar.
- The Claude Code statusline can start with the folder's name; nothing
  new is stored.

Preserved (10.1.4.4): (a) trademark disclaimer on every surface naming
Claude, specific copyright entity, no model names in keywords;
(b) burn-rate projection and pacing, focus hourglass, cooldown game,
themes, Mica, Credential Manager storage; (c) tool-strip navigation
with accessible names (the Settings search box and Quit are now exposed
to screen readers), shortcuts in Settings > Help, native close, dialogs
legible in light and dark mode.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with or endorsed by Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v3.6.0.0)

- [ ] First line names the live version (3.5.0.0 or 3.4.2.0)
- [ ] Version trio in csproj + `<Identity Version>` = `3.6.0.0`; `Sanduhr.Mcp.csproj` at 3.6.0
- [ ] `Package.appxmanifest` declares `runFullTrust` only (`PackageManifestTests` green)
- [ ] What's New cards tagged `3.6.0` show after the update
- [ ] `dist/Sanduhr-Store-v3.6.0.0.msix` built off the `v3.6.0` tag, unsigned; `mcp\sanduhr-mcp.exe` about 13 MB (trimmed); `scripts/smoke-mcp.ps1` passes and lists the five new tools
- [ ] **Watchers off by default on a fresh install**; no `watchers.json` until the switch is used
- [ ] Desk-message suggestions never change `messages.txt` without Add
- [ ] Store keywords: no model names (Fable, Opus, Sonnet, Haiku) — the 3.5.0 failure
- [ ] Trademark disclaimer on all six surfaces plus What's New, the tour and the Desk pages
- [ ] `docs/PRIVACY.md` rows for `watchers.json`, `watch-request-*.json` and the Desk message request/result/state files; the hosted policy at 626labs.dev/privacy.html#privacy-sanduhr matches **before commit**
- [ ] "What's new" through the Store Listing Console from `store-listing-console/apps/sanduhr/copy/whats-new-3.6.0.0.md`; Notes for certification pasted by hand from the block above
- [ ] `dotnet test windows-dotnet` green

## Source

Follows [`reviewer-letter-3.5.0.0.md`](./reviewer-letter-3.5.0.0.md). GitHub release notes:
[`release-notes-3.6.0.0.md`](./release-notes-3.6.0.0.md).
