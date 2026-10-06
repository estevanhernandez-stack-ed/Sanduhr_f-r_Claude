# Sanduhr Time

## Idea
A VS Code coding-time tracker that counts your time and Claude's in one record: per project per day, time split into you, Claude and both, and lines into yours and Claude's, with nothing counted twice. Local-first, network-free, and built to become Sanduhr's coding-time half.

## Who It's For
A builder who codes in VS Code with Claude Code doing much of the writing, often in several sessions at once. WakaTime shows them total time and time per project, but its AI split misses Claude Code entirely (on the reference machine its Claude parser had found zero sessions, because it reads only the default `~/.claude` home). They want to know where the day went and who built what, without sending employer work anywhere under its real name.

## Inspiration & References
- **WakaTime**: the model being replaced. Heartbeats on edit, save and focus; a 15-minute keystroke timeout joins them into time. Its heartbeat API defines fields this project should reuse where they overlap: `entity`, `type`, `category` (including `ai coding`), `time`, `project`, `branch`, `language`, `lines`, `ai_line_changes`, `human_line_changes`, `ai_session`, `is_write`. [Developer API](https://wakatime.com/developers), [AI dashboard](https://wakatime.com/ai).
- **Wakapi**: a self-hosted, WakaTime-compatible backend. Proof that the heartbeat format is a de facto standard; matching its field names keeps an export path open. [Wakapi on Docker Hub](https://hub.docker.com/r/n1try/wakapi).
- **WakaTime data export**: daily summaries or raw heartbeats as JSON, which is the phase 2 import source.
- **Claude Code edit behavior**: edits are written straight to disk, bypassing the VS Code buffer, so `onDidSaveTextDocument` does not fire for them ([issue write-up](https://claudeissues.com/issue/62900-vs-code-save-events-not-triggered-on-claude-code-file-edits)). Open files are reloaded from disk instead, which is the event the reload-subtraction rule targets.
- **RoRoRo streamer mode**: persistent fake names from a pool, assigned lazily per key, rerollable, real map kept locally. The alias model here copies it.
- **Sanduhr**: already reads Claude Code transcripts and publishes daily usage per project; its network-free MCP server and snapshot seam are the eventual home for the day files.

Design energy: dark by default following the VS Code theme, clean, high contrast, visible accents. Stacked you/Claude/both bars carry the panel.

## Goals
- Uninstall WakaTime after two side-by-side weeks without losing anything that was being looked at.
- Get the AI-versus-me split right for Claude Code, including parallel sessions.
- Keep the wall: an unbound or masked project never leaves the machine under its real name.
- Leave Sanduhr a clean handoff: a documented usage spec and schema, so taking over the merge is a swap.

## What "Done" Looks Like
- The status bar shows today's time; hovering shows you, Claude and both.
- The Time panel shows today and the last 7 days per project: stacked split bars, lines, top languages, streamer-mode toggle, per-project mask and reroll, and the WakaTime comparison during the side-by-side weeks.
- `~/.sanduhr/time/` holds the spool, day files and alias map, conforming to `docs/time-usage-spec.md` and `docs/time-day.schema.json`.
- The 626 Labs extension publishes each machine's day record through `manage_time`, under the wall's naming rule, and the bulletin shows a coding line next to Claude usage.
- Installed as `.vsix` on each machine, tests green, a real day recorded and checked against WakaTime.

## What's Explicitly Cut
- **The dashboard Time page and the WakaTime import:** phase 2. The panel and bulletin cover daily needs, and the import should run only after the side-by-side weeks prove the numbers.
- **Sanduhr owning the merge:** phase 3, once Sanduhr's feature boost lands. The spec is the contract that keeps it a swap.
- **Editors other than VS Code:** every WakaTime heartbeat on the reference machine came from VS Code.
- **Any network call from the tracker:** publishing belongs to the 626 Labs extension, which already holds the connection and the per-machine keys.
- **File names or paths off the machine:** spool entries hash them, and nothing published carries them.
- **Marketplace listing:** waits for the Sanduhr feature boost; this cycle ships hand-installed `.vsix` files.
- **Goals, leaderboards and team features:** WakaTime extras nobody was looking at.

## Loose Implementation Notes
- Spool heartbeats reuse WakaTime field names where they overlap, with a hashed `entity`, so phase 2's import and any later Wakapi-style export are mappings, not translations.
- Reload detection, first: a change event that leaves `document.isDirty` false is a strong candidate signal for a disk reload, since typing marks the document dirty. Prove it in a real VS Code window before relying on the 2-second rule.
- Transcript reading is incremental by byte offset; the transcript corpus runs to gigabytes.
- Follow the 626 Labs extension's toolchain: TypeScript, webpack, Vitest with a `vscode` stub.
