# Store listing copy — v4.9.0 (shipped)

> Archive of what went to the Store for 4.9.0.0 (certified 2026-11-17 on the first try, submission
> 1152921505702432347, created and committed through the console API; the MSIX and the reviewer
> letter went in by hand in Partner Center). Source of truth is
> `store-listing-console/apps/sanduhr/copy/` (master, bd6e705); this file is the record of what shipped,
> per release-runbook Phase 7 step 8.
>
> A feature release (5.0 spec 3, every agent's activity: Codex and the Antigravity CLI beside Claude
> Code's local burn), the last 4.x: only the what's-new field changed against 4.8.0.0 (`releaseNotes`,
> 1440 characters), closing with the Anthropic, OpenAI and Google trademark statements. Description,
> features, keywords and screenshots are unchanged. x64 only; arm64 is planned for 5.0. The hosted
> privacy policy took the Codex and Antigravity CLI activity paragraph first (hub #316).

## What's new in this version (en-us)

```
Version 4.9

- Every agent's tokens, side by side. Codex and the Antigravity CLI
  now count beside Claude Code: Sanduhr reads the logs they already
  write on this PC, so there's no sign-in. Codex follows its switch in
  Settings, Sources; the Antigravity CLI has its own "Activity only"
  switch there, off by default.
- The widget footer shows each agent's new tokens since the last
  check: CC +12k, Codex +3k, Agy +1k.
- The Local burn page covers them all: the 30-day strip stacks each
  day by agent, each project says which agents it came from, and the
  session ledger labels and filters by agent.
- Claude can read an agent's project totals through the MCP only after
  you turn on that agent's switch in Settings, Claude Code, Folders.
  Each is off by default.

Antigravity's limits and the Antigravity IDE's usage aren't readable,
so only the CLI's activity counts.

Nothing new leaves your PC: 4.9 adds no network destination. The
counts are read on this PC and kept in Sanduhr's local usage vault,
listed in the privacy policy.

Known issue: if the update won't install (error 0x80073D02), restart
Windows and try again.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, "Codex" is a
trademark of OpenAI, and "Antigravity" is a trademark of Google LLC,
used nominatively. Sanduhr für Claude is an independent third-party
tool, not affiliated with, endorsed by, or associated with Anthropic
PBC, OpenAI or Google LLC.
```
