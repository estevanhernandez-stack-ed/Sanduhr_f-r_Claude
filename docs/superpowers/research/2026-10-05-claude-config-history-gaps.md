# Claude config and history: the gap inventory

Input for a Sanduhr spec: one place to see and manage all of Claude's configuration and history across surfaces, and to recommend the best route when there's more than one. Gathered 2026-10-05 on a Windows machine that runs two Claude Code config homes ("seats"): a personal one and a work one, each its own git repo synced between machines. Evidence tags: **[here]** verified on that machine, **[docs]** official Claude Code / claude.com docs, **[undoc]** not documented and not verified, **[sanduhr]** this repo's code.

## Verdict

Claude's state lives in at least seven places. Each surface manages its own, and nothing manages them as a set. The trigger for this inventory, sessions that "went missing" after a config-home migration, came from three of these gaps compounding: transcripts are machine-local, a 30-day purge runs by default, and the seat sync carries memory but deliberately not sessions. Sanduhr already reads transcripts, writes Claude config safely, and keeps a usage vault that outlives the purge. It's the natural home for a "Claude estate" view, but it has a live bug of its own (G1).

## Where everything lives

| Surface | Config | History | Crosses machines? |
|---|---|---|---|
| Claude Code CLI / VS Code panel | `$CLAUDE_CONFIG_DIR/settings.json` (user), `.claude/settings*.json` (project), managed settings on top [docs] | `$CLAUDE_CONFIG_DIR/projects/<slug>/*.jsonl`, with subagents nested under each session [here] | Only what the seat repo carries: settings, CLAUDE.md, agents, skills, memory. Never `.jsonl` [here] |
| Claude Code state | `$CLAUDE_CONFIG_DIR/.claude.json`: user MCP servers, OAuth, project trust, per-project local MCP [docs][here] | `history.jsonl` (prompt history), `file-history/` (checkpoints, tens of MB) [here] | No (machine-local by design) |
| Seats on one machine | `~/.claude-personal` (personal, about 2.4 GB of transcripts) and `~/.claude-<work>` (work, about 430 MB) [here] | Same layout in each | Each is a git repo, synced by a seat script at session start and end [here] |
| Stray pre-seat home | `~/.claude.json` and `~/.claude/plugins/` left behind by the migration [here] | none | Orphaned after the migration |
| Claude Desktop app | `%APPDATA%\Claude\claude_desktop_config.json`, with its own MCP list (two servers that exist nowhere else) [here]. Path not stated in docs [undoc] | Desktop and Cowork transcripts are stored apart from the CLI's, under their own `desktopSessionCleanupPeriodDays` [docs] | Not verified |
| VS Code extension | `%APPDATA%\Code\User\globalStorage\anthropic.claude-code` (only `session-permission-modes`) [here] | Uses the CLI's transcripts | No |
| claude.ai (web, mobile, Desktop) | Connectors, projects, and server-side skills, styles and preferences | Chats, server-side | Connectors flow one-way into Claude Code, Desktop and mobile for the same account [docs]. Verified as the `mcp__claude_ai_*` tools [here] |

## Gaps

**G1. Sanduhr for Windows is blind to any home that isn't `~/.claude` or `~/.claude-personal`.** (The Mac app already finds every `~/.claude*` home and the `CLAUDE_CONFIG_DIR` one: `mac/Sources/Sanduhr/Services/ClaudeCodeFolders.swift`, a home being any such folder with `projects/` or a `.claude.json`. It does not skip backups yet, so a `~/.claude-*.config-backup-*` copy would show up as a home. Port its rule to Windows and add the backup skip to both.) The Windows app reads a fixed list (`CcLogReader.cs:60,89-99`, repeated in `McpConfig.cs:65`, `StatuslineInstaller.cs:27`, `McpIntegrationInstaller.cs:34`), and never reads `CLAUDE_CONFIG_DIR` [sanduhr]. A migration that moved the work seat to its own `~/.claude-<work>` folder and emptied `~/.claude` made all of that seat's burn, model usage and vault rows disappear from the migration date on. **Fix first, independent of the feature:** discover homes as any `~/.claude*` directory that has a `projects/` subfolder, plus `CLAUDE_CONFIG_DIR`, and skip backups (`*.config-backup-*`).

**G2. The 30-day transcript purge.** The documented default is "unset", which in practice is a built-in 30 days. The purge deletes `projects/*/*.jsonl` and leaves `memory/` alone [docs]. When it runs is undocumented [undoc]. Locally, each seat writes a `.last-cleanup` timestamp at session start [here], and the cleanup skips subagent sidecars: 408 subagent files outlived their parent sessions [here]. Setting `cleanupPeriodDays: 3650` on both seats worked: the next cleanup kept a session over 30 days old [here]. Sanduhr hardcodes the 30-day assumption (`CoverageMarginDays = 25`) and doesn't know the setting exists [sanduhr]. **Sanduhr should** read `cleanupPeriodDays` per seat, show "transcripts older than N days get deleted at next start", warn when a seat is still on the default, and point out that `desktopSessionCleanupPeriodDays` is a separate setting that also needs raising.

**G3. Transcripts never cross machines.** By design: the seat's never-list includes `.jsonl` [here], and the docs offer no cross-machine sync or backup [docs]. In practice this reads as "lost sessions": months of work done on one machine were purged at 30 days while another machine's history stayed put. The chosen direction is a private session vault keyed by normalized repo remote, synced over the local network, never committed to any repo. **Sanduhr could** host it, since it already ingests transcripts per home into an indefinite vault (`VaultStore.cs`, `VaultIngester.cs`). Today that vault keeps usage rows only. The session vault would add the raw `.jsonl` next to them.

**G4. No size story.** With 3650-day retention, transcripts (2.4 GB in one seat today) only grow. **Sanduhr should** show per-seat and per-project size and growth, and offer to archive old sessions (compress and move out of `projects/`) rather than delete them.

**G5. Duplicate project keys in `.claude.json`.** The same path is stored twice, differing only by drive-letter case (`C:/…` vs `c:/…`): 11 duplicates in one seat, 2 in the other [here]. That hides project-scoped MCP servers. PowerShell's `ConvertFrom-Json` refuses to parse the file without `-AsHashtable` [here]. **Sanduhr should** detect these and show which copy holds the trust, MCP and allowed-tools state. Offering a merge has to wait for G9: the file is Claude Code's internal state, so a merge must back up first and run only with Claude Code closed.

**G6. Leftover files in the config homes.** Orphaned `.claude.json.tmp.*` files, several `.claude.json.*bak*` variants, Sanduhr's own backups of `.claude.json` and `settings.json`, a `statusline.ps1.bak`, and the stray `~/.claude.json` [here]. **Sanduhr should** list them with age and origin, and offer cleanup, its own backups first.

**G7. MCP servers in four places, and nothing says which route is best.** User scope in `.claude.json` (local stdio servers), local scope per project, the Desktop app config, and claude.ai connectors [here]. The route rules Sanduhr should recommend:
- **Remote (HTTP) server you want everywhere, including web and phone → add it as a claude.ai connector.** It flows one-way to Claude Code, Desktop and mobile for that account [docs]. Caveat: per account, so a work account doesn't get personal connectors. For people who keep work and personal apart, that's the wall working as intended.
- **Local stdio server → user scope in each seat's `.claude.json`.** Connectors can't run local processes. This is per machine and seats typically don't sync `.claude.json`, so it has to be added on every machine.
- **Team or repo-specific → `.mcp.json` in the repo.**
- **Desktop-app-only entries are a smell:** they don't reach the CLI. Recommend moving them to connectors if remote.
- Secrets never go inline: env vars or the OS keychain.

**G8. Settings drift between seats.** Keys only in one seat: `model`, `effortLevel`, `alwaysThinkingEnabled`, `disabledMcpjsonServers`. Only in the other: `modelSettings`, `pluginConfigs`, `claudeMdExcludes` [here]. Some of this is intended (per-machine model pins kept out of a shared baseline on purpose). **Sanduhr should** show a side-by-side diff and let the person mark each difference as intended.

**G9. The transcript format is unstable.** The docs say the entry format "is internal to Claude Code and changes between versions, so scripts that parse these files directly can break on any release". They recommend `/export` or `claude -p --resume <id> --output-format json` [docs]. Sanduhr already parses `.jsonl` directly [sanduhr], so this risk already exists today. **The vault should** store raw `.jsonl` (lossless, resumable) and treat parsed views as rebuildable. The parser should fail soft per line and report "N lines unreadable since version X" instead of silently dropping usage.

**G10. Keeping old sessions has costs.**
- **Retired models:** on `--resume`, the original model is not restored if it has been retired or isn't in `availableModels`. The session continues on the current default [docs]. Context written for an older model's behavior then runs on a different model.
- **Thinking blocks across model changes:** undocumented [undoc]. Test it before promising that "resume any old session" works.
- **Version skew:** a transcript from an old Claude Code build may not resume cleanly on a newer one [undoc]. That follows from G9 but hasn't been observed.
- **Secrets accumulate:** transcripts capture whatever passed through tools: cookies, tokens, connection strings. Longer retention means more exposure, so a vault stays private and local-network only.
- **Work and personal must not mix:** work transcripts must never land in a personal vault, and must never go through a model pass that can route to another machine. A local model cluster that sends requests to whichever node has the model can carry a work transcript onto a work-owned machine, or a personal one onto a work machine.
- **Disk:** see G4.

**G11. claude.ai and server-side state are opaque locally.** Which claude.ai skills, preferences and memory reach Claude Code is only partly documented [docs][undoc]. Sanduhr already holds a claude.ai session (usage and routines APIs, `ClaudeApiClient.cs`) and lists claude.ai "Reflect" data as a possible vault source [sanduhr]. A read-only list of connectors and their auth state is the useful first step. A session can start with several connectors needing auth and one failing on an expired token, and nothing surfaces that outside the session banner [here].

## What the feature looks like

A "Claude estate" panel in Sanduhr, both apps. The Mac app already reads the homes (local burn per project, model usage, the MCP, statusline and notch-glow installers, account links per home), so it has the discovery half; Windows has the vault. Shared rules (discovery, retention, MCP routes) belong in the pure cores of both:
1. **Homes:** every discovered config home, its seat repo state (branch, unpushed, last sync), its retention setting with a warning on the default, and its transcript size and growth.
2. **History:** sessions per project per machine, what's at risk at the next cleanup, and session vault status once it exists.
3. **MCP routes:** every server from all four sources, deduplicated by name, with a recommended route and auth state.
4. **Housekeeping:** case-duplicate projects, leftover tmp and backup files, the stray home, settings drift.
5. **Writes** follow the existing statusline and MCP install pattern: back up first, only touch Sanduhr's own entries, safe remove [sanduhr]. Anything touching `.claude.json` runs only with Claude Code closed.

## Open questions

- When does the cleanup run relative to SessionStart hooks? It decides whether a seat sync can deliver a retention change before the first purge [undoc].
- `desktopSessionCleanupPeriodDays`: its default, and whether Desktop and Cowork transcripts have already been purged.
- Can a local model cluster pin a request to one node? This gates any model pass over transcripts.
- Does resuming a months-old session on a newer Claude Code build actually work? Test with a surviving subagent file's parent once the vault holds one.
