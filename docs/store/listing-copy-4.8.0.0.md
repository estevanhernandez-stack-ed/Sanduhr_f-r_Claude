# Store listing copy — v4.8.0 (shipped)

> Archive of what went to the Store for 4.8.0.0 (certified 2026-11-12 on the first try, submission
> 1152921505702393638, created and committed through the console API; the MSIX and the reviewer
> letter went in by hand in Partner Center). Source of truth is
> `store-listing-console/apps/sanduhr/copy/` (master, 93660b6); this file is the record of what shipped,
> per release-runbook Phase 7 step 8.
>
> A feature release (5.0 spec 2, the agent layer: Codex limits beside Claude's): only the what's-new
> field changed against 4.7.0.0 (`releaseNotes`, 1473 characters), closing with both the Anthropic and
> OpenAI trademark statements. Description, features, keywords and screenshots are unchanged. The
> hosted privacy policy took the Codex limits paragraph first (hub #313).

## What's new in this version (en-us)

```
Version 4.8

- Codex limits, beside Claude's. Turn on Codex in Settings, Sources
  and the widget shows its 5-hour and weekly limits under Claude's.
  They're read from the logs Codex already writes on this PC, so
  there's no sign-in. Each says when Codex last wrote; after 30 quiet
  minutes it dims and drops its pace, so an old number never reads as
  current.
- Both plans on one bar: it fills to your primary plan and carries on
  in the other plan's color when that one is further along. Pick the
  primary, each plan's color and its alerts on the new Sources page.
  The tray follows the primary.
- Desk meters can show Combined, Claude or Codex, and the MCP's
  get_usage and the Claude Code statusline include Codex too.
- Name your own codenames: Settings, Privacy, Show the real name,
  Rename picks what streamer mode shows for each account, folder or
  repo.
- A widget pack zip you made yourself installs, and installing the
  MCP server checks that it starts.

Nothing new leaves your PC: 4.8 adds no network destination. Codex's
limits are read on this PC and kept in two local files, listed in the
privacy policy.

Known issue: if the update won't install (error 0x80073D02), restart
Windows and try again.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, and "Codex"
is a trademark of OpenAI, used nominatively. Sanduhr für Claude is an
independent third-party tool, not affiliated with, endorsed by, or
associated with Anthropic PBC or OpenAI.
```