# Mods & Config

Spec, 2026-10-07. Follows item 64 (the Mods page, PR #161). Research inputs: `docs/superpowers/research/2026-10-05-claude-config-history-gaps.md` (gaps G1 to G11 and the estate panel sketch), `docs/superpowers/research/2026-10-05-knowledge-carry-review.md`, `docs/superpowers/research/2026-10-06-claude-code-mods.md`, and four research lenses run on 2026-10-07 (the personal seat repo, the work seat by mechanism only, the current Claude Code docs against CLI 2.1.293, and Sanduhr's own Mac code on `feat/mac-batch-2`).

Evidence tags: [docs <url>] for code.claude.com, [repo <repo>:<path>] for a file in a repo, [local <path or command>] for something observed on the owner's machines, [unverified] for anything not checked.

## Verdict: one page per seat that reads everything first and writes only through receipts

Mods & Config turns Settings, Mods into the place where a person who runs more than one Claude Code config folder (a "seat") sees what each seat actually loads, why, and from which file, and changes the safe parts with an exact undo. It is for the owner first: several seats, each a git repo synced between machines, a never-list of files that must not leave a machine, and a strict wall between a work seat and a personal one. It is built so that anyone with a single `~/.claude` gets a useful, quieter version of the same page.

The page is read-first by design. Claude Code already has one writer for most of its state (the `claude plugin` and `claude mcp` CLIs, `/config`), the seat repos already have their own sync, hold rules and push gate, and Claude Code rewrites `.claude.json` by itself at any moment. Sanduhr's job is to make all of that visible, explain precedence, and carry out a small set of edits that it can undo byte for byte. It never becomes a second sync engine.

Build order:

1. **Fix the ground first.** The `.claude.json` placement rule currently resolves the default seat to a 423-byte stub instead of the live file, which breaks MCP status, account suggestions, project discovery and Remove [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/ClaudeCodeFolders.swift:37-45] [local stat and `jq keys` of both files, values not printed]. Seat discovery also needs the `.claude-backup-*` skip and a manual "Add folder…".
2. **Slice 1, read-only.** Every section of the page below, for every seat, with no write path except the existing Sanduhr mod switch.
3. **Slice 2, safe edits.** A general config receipt and edits for a short list of scalar, secret-free keys (retention, claude.ai sync switches, connector switch, `claudeMdExcludes` entries).
4. **Slice 3, CLI-routed edits.** Plugins and MCP servers through `claude plugin ... --json` and `claude mcp ...` with the seat selected by `CLAUDE_CONFIG_DIR`, gated on no running session for that seat.
5. **Slice 4, the seat panel.** Git state, the seat's own sync status, held files, structural pushes waiting, the kill switch, a dry run, all by reading the seat's files or running the seat's own scripts.
6. **Slice 5, drift and housekeeping.** Differences between seats and between a seat and its repo, leftovers and duplicates, history archive.

## Principles

### Read before write, always

Every section ships read-only first and stays useful that way. A write appears in a later slice only after its read view has run on the owner's seats long enough to show the model is right. The read path never runs user code: no mod, hook or MCP server is started to find out what it is (the same rule ModInventory follows today) [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/ModInventory.swift].

### Every edit goes through a receipt, with exact undo and a backup

Sanduhr already has the right engine: JSONEdit splices one member into the text so every other byte stays; a write is planned in memory, re-parsed and verified to equal the original minus the one member, committed as compare-and-swap (re-read, abort and retry up to three times if the file changed), written to a hidden sibling with the original permissions and renamed over the file, through symlinks to their target [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/JSONEdit.swift] [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/IntegrationInstaller.swift:1026-1073]. Receipts record what was created and the previous bytes, so Remove returns the file byte for byte [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/IntegrationInstaller.swift:57-140]. Mods & Config adds one receipt kind, `configEdit`, keyed by (seat, file, key path), for keys Sanduhr does not own. Backups live in Sanduhr's Application Support folder, not next to the file in the seat, so a seat repo never sees them (see Risks).

### Prefer Claude Code's own CLI where one exists

Plugins and MCP have scope-aware CLIs: `claude plugin install|uninstall|enable|disable|update -s user|project|local --json` and `claude mcp add|add-json|remove -s ...` [local `claude plugin --help`, `claude mcp --help`] [docs https://code.claude.com/docs/en/plugins/cli-reference] [docs https://code.claude.com/docs/en/mcp]. The `--json` results carry a `failureCode` (`already_in_goal_state`, `settings_still_on`) that a UI can act on without scraping text [docs https://code.claude.com/docs/en/plugins/cli-reference]. Sanduhr runs them with `CLAUDE_CONFIG_DIR` set to the seat, the same hardened runner ModCheck uses (cwd in a temp dir, stdin null, `NO_COLOR`, 10 s timeout, then SIGTERM and SIGKILL) [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/ModCheck.swift:180-254]. For keys with no CLI writer and no `/config` row (`env`, `hooks`, `cleanupPeriodDays`, `claudeMdExcludes`, most of `permissions`), editing the settings file is the documented path: `/config` "lists a short set of personal options ... not every settings key" [docs https://code.claude.com/docs/en/settings#use-the-/config-menu] [docs https://code.claude.com/docs/en/settings#edit-a-settings-file]. Sanduhr's file edit goes through a receipt.

Sanduhr calls only subcommands it has checked against `claude --help` of the installed version. 2.1.293 has no `config` subcommand [local `claude --help`, Commands list], so `claude config list` is read as a prompt: it starts a model turn, spends usage and exits 0 [local `cd /tmp && claude config list`, observed by the earlier lens, not re-run in verification to avoid spending usage]. An unknown subcommand is never run.

### Never touch `.claude.json` while Claude Code runs

`.claude.json` holds the sign-in session, user and local MCP servers, per-project state such as trust decisions, and the global config keys `/config` writes; Claude Code "writes it for itself", and saves a timestamped backup to `backups/` before each write [docs https://code.claude.com/docs/en/settings#find-or-create-your-settings-files] [docs https://code.claude.com/docs/en/settings#fix-a-broken-settings-file]. With `CLAUDE_CONFIG_DIR` set it lives inside the seat folder, so each seat has its own [local `stat` of `~/.claude-personal/.claude.json` with the variable set: present, has `oauthAccount`]; the docs say only that settings, history and plugins move under that path [docs https://code.claude.com/docs/en/env-vars]. Sanduhr reads it any time and writes it never by hand. The CLI routes that write it (`claude mcp add|remove`, `claude purge`) run only when no Claude Code process is running; until per-seat detection is proven, any running `claude` process blocks every seat [unverified: reading another process's `CLAUDE_CONFIG_DIR` on macOS]. `claude mcp list` and `get` health-check servers, which spawns stdio servers and makes network calls, so the page reads files instead of calling them [local `claude mcp list --help`] [docs https://code.claude.com/docs/en/mcp].

### Never show or store a secret

Values under `env`, and under any key path segment naming token, key, secret, password or credential, are masked everywhere: in the settings view, in diffs, in receipts' display, in logs and in `state.yaml`. This follows the seats' merge driver, which logs a sensitive path as "changed on both machines, newer side kept" with no value; its matcher is `env` plus `token|api[_-]?key|secret|password|credential`, so Sanduhr's bare `key` is the wider of the two [repo dotclaude-personal:.seat/lib/jsonmerge.ps1:26-30,100-102]. MCP headers and `-e` values are shown by name only, as Claude Code's own surfaces show a `${VAR}` reference by name rather than its resolved value [docs https://code.claude.com/docs/en/mcp]. `.credentials.json`, the Keychain entries and `oauthAccount` are never read for display. Receipts may hold a previous value for undo only when the key path is not secret-named; a secret-named key is not editable from the page at all.

### The wall is a feature, not a filter

A seat can be marked **work**. The mark comes from the account link Sanduhr already has (one account per folder, a `work` flag per account) [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/AccountData.swift:47-60,160-185] or from a per-seat toggle when no account is linked. With the mark set:

- Nothing crosses from it. No copy, export, "apply to other seat" or sync action exists with a work seat as the source and a personal seat as the target. The control is absent, not disabled with a tooltip.
- Drift between a work seat and a personal one shows key names and "differs" only, never values.
- The seat's label is user-chosen. Sanduhr never displays a tenant name read from the seat's own files (the seat identity file's name field may be the employer's name), so screenshots and demo mode are safe.
- Writes to a work seat ask once more, naming the seat.
- Work-seat rows hide in demo mode (the mode screenshots are taken in), as watchers already do [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/WatcherStore.swift:186-190] [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/Watchers.swift:330-332].
- Wall controls are first-class per seat: `syncClaudeAiSkills: false`, `syncClaudeAiPlugins: false`, `disableClaudeAiConnectors: true`, and `deniedMcpServers` with a `serverName` entry holding the connector's display name (such as `"claude.ai Slack"`), because account-wide claude.ai skills, plugins and connectors otherwise flow into every seat signed in to that account [docs https://code.claude.com/docs/en/skills] [docs https://code.claude.com/docs/en/plugins/loading#synced-plugins] [docs https://code.claude.com/docs/en/mcp] [docs https://code.claude.com/docs/en/settings-reference#deniedmcpservers]. The two sync switches are honored from user, local or managed settings only; a `false` in a project's `.claude/settings.json` is ignored [docs https://code.claude.com/docs/en/settings#exceptions-to-managed-settings-precedence], which is why Sanduhr writes them to the seat's own `settings.json`.

The seats' own wall (an identity file checked against the remote, a prompt-blocking guard, a memory hold keyed on the work projects folder, a capability-based pre-commit check) stays the enforcement. Sanduhr shows its results and never reimplements it [repo dotclaude-personal:.seat/README.md] [local work seat repo: `.githooks/pre-commit`, decision log entry "the wall is enforced on capability, not on the string"].

## The page, seat by seat

Settings, Mods becomes Settings, **Mods & Config**. A seat picker sits at the top: one chip per discovered seat with its user-chosen label, a work or personal badge in a fixed color, and a dot for "Claude Code running". Below it, eight sections for the selected seat. Each section has a one-line summary that is visible collapsed, so the page reads as a health report before anything is opened.

The seat header carries: the folder path (shortened to `~`), the linked account, the install model (git clone, copy-installed from a repo elsewhere, or plain folder), the Claude Code version found on PATH, and whether this seat is the one `CLAUDE_CONFIG_DIR` points at for GUI launches. Sanduhr runs from the Dock and never sees the shell's `CLAUDE_CONFIG_DIR` [unverified: standard launchd behavior, not tested here], so "Add folder…" (already on Integrations) joins this page.

### Settings: readable controls with their source and precedence

Every key in effect for the seat, grouped by the docs' families (model and responses, permissions, env, hooks, plugins, MCP approval, memory, attribution, sandbox, UI, privacy, updates) [docs https://code.claude.com/docs/en/settings-reference]. Each row shows:

- the effective value (masked per the secret rule), rendered as a control where the type is simple (a toggle, a number, a pick list) and as formatted JSON otherwise;
- **where it comes from**: managed, project local, project shared, or user, with the file named, computed by Sanduhr by walking managed, then each project's `.claude/settings.local.json` and `.claude/settings.json`, then the seat's `settings.json`, applying list merge for arrays (with the documented exceptions `fallbackModel`, `modelPicker`, `availableModels`, `modelSettings`), per-plugin-id resolution for `enabledPlugins`, and the strict-wins exceptions (`disableClaudeAiConnectors`, `maxEffortLevel`, `permissions.blockReadsOutsideWorkingDirectories`, `enableArtifact`, `isolatePeerMachines`, the two `syncClaudeAi*` switches and the rest of the table) [docs https://code.claude.com/docs/en/settings#settings-precedence] [docs https://code.claude.com/docs/en/settings#lists-merge-instead-of-overriding] [docs https://code.claude.com/docs/en/settings-reference#enabledplugins] [docs https://code.claude.com/docs/en/settings#exceptions-to-managed-settings-precedence]. The docs' stack also has a command-line level (`--settings`) between managed and project local, and shell variables that override some keys pair by pair; Sanduhr cannot see either for a session it did not start, so the view says so. Labelled "computed by Sanduhr", because `/status` lists which files loaded, not which file supplied each key. ModOverrides already does this for one `enabledPlugins` key and is the seed [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/ModSwitch.swift:40-111];
- **where it is honored**: a badge from the docs' Scope column, and a warning when a key sits where it is silently ignored (`pluginConfigs`, `appendPlugins`, `modelPicker`, `autoMode` outside user or managed; `permissions.defaultMode` of `auto` or `bypassPermissions` in a project or local file; `CLAUDE_CONFIG_DIR` in a project or local `env`; `syncClaudeAiSkills` or `syncClaudeAiPlugins` false in a shared project file; `mcpServers` in `settings.json`; `permissions`, `hooks` or `env` in `.claude.json`) [docs https://code.claude.com/docs/en/settings-reference] [docs https://code.claude.com/docs/en/settings#a-value-you-set-is-ignored] [docs https://code.claude.com/docs/en/env-vars] [docs https://code.claude.com/docs/en/debug-your-config#check-common-causes];
- **this machine only**, when the seat lists the key in `.seat/local-keys.json` (today `model`), which the seat's clean filter strips before commit [repo dotclaude-personal:.seat/local-keys.json].

Known dead config gets a lint line: `Write(path)` allow rules never match (only `Edit(path)` does) [repo dotclaude-personal:.seat/docs/superpowers/specs/2026-09-29-same-surface-spike-results.md item 12]; a `settings.local.json` inside the seat folder is not read at user scope, so hooks and permissions there are inert, except that a session started in the home folder can read `~/.claude/settings.local.json` as that folder's project local file [unverified: inferred from the project-file rule, not tested] [same file, item 1; the docs list no user-scope local file, only the project's `.claude/settings.local.json`, docs https://code.claude.com/docs/en/settings#settings-files-and-who-they-affect, docs https://code.claude.com/docs/en/settings#where-claude-code-keeps-the-local-file-in-a-git-repository]; Windows-only absolute paths in a seat opened on a Mac.

A `settings.json` that does not parse shows as **unreadable** with the parse error position, never as empty. Today ModInventory reads it with `try?` and a broken file looks like a seat with nothing in it [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/ModInventory.swift:336-340].

### Instructions: CLAUDE.md and what it pulls in

The instruction stack a session in this seat would load: the managed CLAUDE.md (the file, or a `claudeMd` key in managed settings), the seat's `CLAUDE.md`, user `rules/*.md` with their `paths` gates, and each `@path` import resolved up to the four-hop limit, drawn as a tree with line and byte counts [docs https://code.claude.com/docs/en/memory]. Imports that point outside the seat are marked, and so are imports that do not resolve. `claudeMdExcludes` is listed with what each entry actually excludes on this machine. A warning appears when `~/.claude/CLAUDE.md` would load as an ancestor file into sessions of a different seat, the leak the personal seat hit before it added excludes [repo dotclaude-personal:.seat/docs/superpowers/specs/2026-09-29-same-surface-spike-results.md items 6, 11]. Auto memory shows per project: the `MEMORY.md` index size against the 200-line and 25 KB load limit, and index lines that point at missing files.

### Extensions: agents, skills, commands, output styles, plugins and mods

The existing Mods page folds in here unchanged as the **Mods** subsection: inventory, risk card from `claude plugin validate --json`, the Sanduhr mod switch with its receipt [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Views/ModsSettings.swift]. Around it:

- **Agents, skills, commands, output styles**: one row each, with name, description from frontmatter, scope (seat, plugin, project) and a shadowing note when a higher scope wins on the same name [docs https://code.claude.com/docs/en/sub-agents] [docs https://code.claude.com/docs/en/skills] [docs https://code.claude.com/docs/en/output-styles]. claude.ai-synced skills (`skills/synced/`) are labelled as arriving from the account and overwritten on the next sync. `skillOverrides` states are shown.
- **Plugins**: enabled, installed and declared, side by side. The plugin parity checks the personal seat designed but has not shipped: enabled but not installed on this machine, enabled twice from two marketplaces (six such pairs in the personal seat today), a marketplace with no declared source, a directory marketplace whose path exists on one machine only [repo dotclaude-personal:.seat/docs/superpowers/specs/2026-09-30-plugin-parity-design.md] [repo dotclaude-personal:settings.json]. `claude plugin details <name>` supplies a projected token cost column on demand [docs https://code.claude.com/docs/en/plugins/cli-reference]. An inline mod that shadows a same-named marketplace plugin is flagged, because `claude plugin list` still shows the marketplace row as enabled [docs https://code.claude.com/docs/en/plugins/loading]. `@synced` plugins appear even with no `enabledPlugins` key (today they are missed) [repo Sanduhr_f-r_Claude:docs/superpowers/research/2026-10-06-claude-code-mods.md].
- **Hooks**: every hook from settings, plugins and frontmatter, merged as Claude Code merges them, with the event, matcher, command (paths shortened) and timeout. `/hooks` is a read-only browser and "there is no way to disable an individual hook while keeping it in the configuration" [docs https://code.claude.com/docs/en/hooks#the-/hooks-menu] [docs https://code.claude.com/docs/en/hooks#disable-or-remove-hooks], so this view is read-only too, except Sanduhr's own hooks.

### MCP servers: every source, and the recommended route

One list across all six sources: the three scopes local (per project in `.claude.json`), project (`.mcp.json`) and user (`.claude.json`), then plugin servers and claude.ai connectors, in that precedence order, with managed servers (`managedMcpServers`, `managed-mcp.json`) above all of them. On a collision the whole entry comes from the highest source, no field merge; the three scopes match duplicates by name, plugins and connectors by endpoint [docs https://code.claude.com/docs/en/mcp#scope-hierarchy-and-precedence]. Per server: transport, command or URL host, env and header names (never values), approval state (`enabledMcpjsonServers`, per-project `/mcp` toggles) and the scope it should live in. The recommended route comes from G7: local stdio servers at user scope, project servers in `.mcp.json` with `${VAR}` placeholders, remote services as claude.ai connectors where one exists [repo Sanduhr_f-r_Claude:docs/superpowers/research/2026-10-05-claude-config-history-gaps.md G7]. Duplicates by endpoint under different names get flagged, because allow rules keyed to one name miss the others [repo dotclaude-personal:.seat/docs/superpowers/specs/2026-09-29-same-surface-design.md]. Connectors appear with their auth state when Sanduhr's claude.ai session can list them (G11) [unverified: a connectors endpoint in the claude.ai API Sanduhr uses].

### History: retention, size, archive

The 30-day purge is the headline. When `cleanupPeriodDays` is unset the seat shows, in warning color, "Transcripts older than 30 days are deleted at the next start" (the default is 30; the deletion is a silent background sweep after a session starts), with the value of `desktopSessionCleanupPeriodDays` (Claude Desktop and Cowork transcripts, user or managed scope) beside it [docs https://code.claude.com/docs/en/settings-reference#cleanupperioddays] [docs https://code.claude.com/docs/en/settings-reference] [repo Sanduhr_f-r_Claude:docs/superpowers/research/2026-10-05-claude-config-history-gaps.md G2]. Below: transcript size per seat and per project with growth over 30 days, the oldest session, subagent sidecars that outlived their parent, `history.jsonl` size (kept until deleted, except under the HIPAA configuration) [docs https://code.claude.com/docs/en/claude-directory#kept-until-you-delete-them], and the vault's coverage of each project. Sanduhr's hardcoded 25-day coverage margin becomes the seat's real retention minus a margin. The archive action (slice 5) compresses sessions older than a chosen age into Sanduhr's vault folder, outside every seat, and leaves `memory/` untouched (G3, G4).

### Housekeeping: leftovers and duplicates

A list with age, size and origin for each item, Sanduhr's own first:

- Sanduhr's `*.sanduhr-backup` files. The one beside `~/.claude.json` is a full stale copy of Claude Code state that never self-deletes, because the file never returns byte-identical [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/IntegrationInstaller.swift:1053-1057] [local `stat` of the backup files].
- Orphaned `*.tmp.*` siblings, stray `.claude.json.backup*` and `*.bak*` variants outside `backups/` (Claude Code keeps its own five most recent `.claude.json.backup.<timestamp>` in the seat's `backups/` folder, so those are not leftovers [docs https://code.claude.com/docs/en/settings#fix-a-broken-settings-file]), `*.config-backup-*` and `.claude-backup-*` folders, a stub `.claude.json` beside a live one, a `bridge-spawn/` folder left by `claude rc` [repo dotclaude-personal:.seat/docs/superpowers/specs/2026-09-29-same-surface-spike-results.md item 9].
- Duplicate project keys in `.claude.json` (path case variants) with which copy holds trust, MCP and allowed tools (G5), and case-twin memory folders.
- Plugins enabled twice, dead allow rules, imports that do not resolve.

Deleting a file Sanduhr created is one click. Anything else moves to the Trash, never `rm`, and the list states what Claude Code would do with it if left alone (the docs' swept versus kept-until-deleted split) [docs https://code.claude.com/docs/en/claude-directory].

### Seat: the repo, its sync and its never-list

For a seat that is a git clone, or copy-installed from one: branch, ahead and behind against `origin/main`, the last commit's machine and summary (the seats write `seat(<machine>): ` followed by the paths, each prefixed `+` added, `~` changed or `-` deleted; hand commits use other prefixes such as `chore(<machine>):`) [local `gh api repos/<owner>/dotclaude-personal/commits`, subjects only], the remote checked against the seat identity file (`.seat/seat.json`) with a mismatch shown as a hard error [repo dotclaude-personal:.seat/seat.json], and the seat's last sync read from `.seat/state.json` (last run, phase, paused, last report lines, apply steps seen) without running git [repo dotclaude-personal:.seat/README.md "Commands"]. Held files with their reason and the one fix; structural commits waiting for a reviewed hand push, with a diff preview; the kill switch (`.seat/disabled`) shown as a state.

The never-list renders as three buckets: **synced** (the allow-list), **machine-only by design** (`settings.local.json`, `.seat/local-values.json`, plugin caches, `.claude.json`, transcripts) and **never** (credentials, env files, keys, logs, transcripts) [repo dotclaude-personal:.gitignore] [local work seat repo: `.seat/lib/guards.ps1`, allow and never patterns]. The buckets come from reading the seat's own `.gitignore` and guard patterns, not from Sanduhr's copy of them, so a docs-versus-reality gap like the work seat's memory reversal (project memory ignored by one commit, tracked again by a later one the same day) shows up as what the files say [local work seat repo: `git log -- .gitignore`, memory lines only].

A folder that is not a git clone shows as **unsynced seat**, which is what the personal folder on the company-managed Mac is today [local `git -C ~/.claude-personal status`: not a git repository]. Sanduhr offers no conversion: the seat's own onboarding (kill switch first, fetch, reset the index, review drift, dry run, enable) is the route, and the page links to it [repo dotclaude-personal:.seat/docs/superpowers/specs/2026-09-29-same-surface-design.md, migration steps].

### Drift between seats

Three comparisons, each read-only:

- **Seat against seat**: a key-level diff of `settings.json`, plus file-level lists of agents, skills, commands, plugins and MCP servers, with a per-difference "intended" mark that Sanduhr remembers (G8). Across the wall, names only.
- **Live against repo**, for copy-installed seats: the live folder against the repo's `origin/main`, key-level for settings, file-level for the rest, as "live only" and "repo only". The work seat on the company-managed Mac drifts this way today: live settings lack several repo keys and carry hooks the repo lacks [local `jq keys` on the live and repo settings files, values not printed].
- **Routing**: which project folders route to which seat through `claudeCode.environmentVariables.CLAUDE_CONFIG_DIR` in `.vscode/settings.json`, with a red flag for a folder under the work projects root that routes to a non-work seat, and the memory keys that would trip the seats' cross-tenant memory hold [local work seat repo: the routing script] [repo dotclaude-personal:.seat/README.md "Held files"].

## Shippable slices

Each slice ships on its own, in the checklist's acceptance and verify style.

### Slice 0: the ground

Placement rule: when both `~/.claude.json` and `~/.claude/.claude.json` exist, prefer the one with `oauthAccount`, then the more recently modified, and show both on the page; Remove uses the file named in the receipt, never the rule. Discovery skips `.claude-backup-*` as it skips `.config-backup-*`, and Mods & Config gets "Add folder…". The receipt decoder reads records one by one, so a bad record is reported and skipped instead of blinding every undo (today the file decodes as one array, all or nothing, and a failure reads as no receipts) [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/IntegrationInstaller.swift:117,1081-1084].

Acceptance: on the owner's Mac, the default seat shows `mcpServers.sanduhr` as installed and its projects; Remove of the MCP entry edits `~/.claude.json` and nothing else; a receipts file with one malformed record still undoes the others.
Verify: swift-testing for the placement rule (stub plus live, live only, inner only, both with oauth), discovery fixtures with backup folders, per-record decode; by hand on the owner's Mac.

### Slice 1: read-only Mods & Config

All eight sections for every seat, reading files only. No new writes. Wall: work badge from the account link or a per-seat toggle (the toggle is a Sanduhr preference, not a seat write), user-chosen labels, work rows hidden in demo mode, cross-wall drift by name only. The secret mask applies to every rendered value.

Acceptance: the owner's seats each show settings with source and honored badges, the instruction tree, extensions with parity checks, MCP across scopes, retention with the 30-day warning, leftovers, seat repo state from `state.json`, and drift; no env value or secret-named value appears anywhere on screen, in logs or in `state.yaml`; an unparseable `settings.json` shows as unreadable; nothing in any seat changes (mtime check over every seat before and after).
Verify: swift-testing for the precedence walker (list merge, `enabledPlugins` per key, strict-wins table), the scope badge table, the mask (env, nested secret paths, MCP headers), the parity checks, the never-list buckets from fixture `.gitignore` files, `state.json` parsing, drift with the wall; a smoke action that opens the page per seat and reports counts in `state.yaml`; by hand on the owner's seats.

### Slice 2: edits with undo, safest keys first

A `configEdit` receipt (seat, file, key path, previous bytes or absent, created parent) and edits for: `cleanupPeriodDays` and `desktopSessionCleanupPeriodDays` (number, minimum 1), `syncClaudeAiSkills`, `syncClaudeAiPlugins`, `disableClaudeAiConnectors` (booleans), and adding or removing one `claudeMdExcludes` entry. All in the seat's `settings.json` only, never in a project file and never in `.claude.json`. Each edit shows the exact diff before it writes and names the consequence for a synced seat ("this change will be committed and pushed by the seat's next sync"). An Undo list per seat. Backups in Application Support.

Acceptance: raising retention on a seat clears the 30-day warning, and Undo returns `settings.json` byte-identical; the wall switches turn on and off with exact undo; an edit while the file changes underneath retries or aborts cleanly; nothing is written beside the file in the seat.
Verify: swift-testing for each key's plan and verify, the receipt round trip, compare-and-swap under a concurrent write, backup location; a smoke action for one edit and its undo; by hand on a synced seat, checking the next sync commits only that key.

### Slice 3: plugins and MCP through the CLI

Plugin enable, disable, install and uninstall through `claude plugin ... --scope <s> --json` with `CLAUDE_CONFIG_DIR=<seat>`, interpreting `failureCode`; MCP add and remove through `claude mcp add|add-json|remove -s <scope>`, with secrets entered into a field that is passed to the CLI and never stored or logged. Every CLI write is blocked while a Claude Code process runs, and the page says why. Sanduhr's own mod switch moves to the CLI if it accepts `@inline` ids [unverified]. Fixes offered by the parity checks (install the missing plugin, disable the duplicate) become one-click CLI actions.

Acceptance: a duplicate plugin is disabled from the page and `claude plugin list --json` agrees; an MCP server added from the page appears in `claude mcp list` for that seat only; no CLI write runs while a session is open; no secret reaches disk outside Claude Code's own store.
Verify: swift-testing with a fake `claude` binary recording argv and env (seat selection, scope, `--json` parsing, refusal while running, refusal of unknown subcommands); by hand with a scratch seat.

### Slice 4: the seat panel

Git state through `git -C <seat or repo>` read commands only (`status --porcelain`, `rev-list --count`, `log -1`), `state.json`, held files, structural pushes waiting with a `git log -p origin/main..HEAD` preview, and two actions that run the seat's own scripts: dry run (`sync -DryRun`) and the wall audit (`.githooks/pre-commit --all`), output shown with matched tokens masked. Pausing sync (creating `.seat/disabled`) is the only write, behind a confirmation. Never commit, push, pull or reset.

Acceptance: a seat with a held file shows the file, its reason and the fix; a structural commit waiting shows its diff; dry run output matches running it in a terminal; the panel never changes the repo except the kill switch.
Verify: swift-testing for parsing fixture `state.json` and porcelain output; a test repo with a held file and a structural commit; by hand on both seats.

### Slice 5: drift actions, housekeeping and archive

Mark drift as intended; copy one key from one seat to another (personal to personal, or personal to work; never from a work seat), through a `configEdit` receipt; housekeeping cleanup (Sanduhr's files deleted, others to the Trash); history archive into the vault folder.

Acceptance: a copied key lands with exact undo; no control offers a copy out of a work seat; cleanup removes Sanduhr's backups and moves the rest to the Trash; an archived session can be restored to `projects/` and resumed.
Verify: swift-testing for the copy direction rule, the cleanup classifier and the archive round trip; by hand.

## Risks and how each is handled

**A concurrent write by Claude Code.** Compare-and-swap with retry for `settings.json`; no hand writes to `.claude.json`; CLI writes only with no session running.

**A secret on screen or on disk.** One mask function used by every view, log and diff, tested against nested and env paths; secret-named keys are not editable; receipts never store a secret-named previous value.

**Crossing the wall.** Copy and sync controls do not exist with a work source; cross-wall drift shows names only; labels are user-chosen; demo mode hides work rows. A seat with no account link defaults to unmarked, so the page asks for a mark on first view of each seat.

**Writes that become synced commits.** Any write to a synced seat's `settings.json` is pushed by the seat's next sync. The edit dialog says so; Sanduhr never writes `.seat/`, `.gitignore`, hooks or apply steps (the seats treat those as structural and hold the push) [repo dotclaude-personal:.seat/README.md "The structural list"].

**Machine-specific paths in synced files.** Sanduhr's MCP, statusline and mod entries carry absolute Mac paths (the python3 and the installed script or mod path; the hook commands use `$HOME` and travel), so a synced `settings.json` on another machine shows them without receipts and pointing nowhere [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/IntegrationInstaller.swift:218-232,250-253]. Slice 1 labels such entries "written on another machine"; a portable path form is an open question.

**Backups and temp files leaking into a seat repo.** Backups move to Application Support; temp siblings are hidden and renamed in the same call. Housekeeping lists any that remain.

**A CLI call with side effects.** Running `claude` with a seat's `CLAUDE_CONFIG_DIR` can create or modify that seat's state files; the stub `.claude.json` may have come from such a run [unverified]. Read paths never call the CLI except `plugin validate` and `plugin details` on demand; every call is logged by subcommand name only.

**Precedence computed wrong.** The view is labelled "computed by Sanduhr" with a link to the docs' table; tests pin each exception; managed sources beyond the one known file are an open question.

**Strict JSON refusing a real seat.** Claude Code requires strict JSON for settings: a `//` comment or a trailing comma is a Settings Error [docs https://code.claude.com/docs/en/settings#edit-a-settings-file], so a file with comments is broken for Claude Code too; the page says so instead of reading it as empty.

**Running seat scripts from the app.** Dry run and wall audit run pwsh or bash from a notarized app. They are on-demand buttons, run with a timeout, and the panel works without them.

**Public repo.** Fixtures and screenshots use invented seats; no test or doc names a real tenant.

## Open questions

1. Which `.claude.json` does Claude Code use for the default seat when both `~/.claude.json` and `~/.claude/.claude.json` exist and no `CLAUDE_CONFIG_DIR` is set, and what created the stub?
2. Does `claude plugin enable|disable` accept `<name>@inline` ids with `--scope`, so Sanduhr's own mod switch can move to the CLI? Partly answered: the docs name `claude plugin disable <name>@inline` as a way to let the installed copy load [docs https://code.claude.com/docs/en/plugins/cli-reference]; `enable` with `@inline` and the scope it writes to are [unverified].
3. Does Claude Code honor `CLAUDE_CODE_PLUGIN_DIRS` from a settings `env` block, or only from the launch environment? ModSwitch relies on the former. [unverified]
4. Can Sanduhr tell which seat a running `claude` process belongs to (its `CLAUDE_CONFIG_DIR`), so a session in one seat does not block writes to another? [unverified]
5. Which managed-settings sources exist on macOS beyond `/Library/Application Support/ClaudeCode/managed-settings.json`? Answered by the docs: the `com.anthropic.claudecode` managed-preferences domain (MDM configuration profile), a `managed-settings.d/` drop-in folder beside the file, `managed-mcp.json` in the same folder, a policy helper, and server-managed settings from the claude.ai console [docs https://code.claude.com/docs/en/managed-settings]. What remains open is how many of these Sanduhr can read without privileges; the console source it cannot read at all [unverified].
6. Does `claude plugin list --json` under a seat's `CLAUDE_CONFIG_DIR` refresh marketplaces or write `.claude.json`? If yes, the inventory stays file-based. [unverified]
7. Should receipts travel with a seat (a never-listed sidecar) or stay per machine with a "re-adopt entries written on another machine" flow?
8. Should Sanduhr's entries use a portable path form so one synced `settings.json` works on every machine of a seat?
9. Should the page read `.seat/state.json` directly (fast, couples to its schema) or always call the seat's `-Status`? The proposal is to read the file and fall back to "unknown schema" on a mismatch. Do the personal and work seats share the same schema? The shared-tooling manifest says the sync library is identical, not verified for the state file.
10. A personal config folder exists on the company-managed Mac against the earlier "work seat only on company hardware" doctrine, and the work seat's own rules were later relaxed to allow the owner's personal context everywhere. Should the page show both seats side by side on that machine, or warn?
11. Does a ConfigChange hook payload carry enough (source, file path) for Sanduhr to refresh the page live? The payload carries `source` (`user_settings`, `project_settings`, `local_settings`, `policy_settings`, `skills`) and optionally `file_path`, and fires only inside a running session, not for MDM or console deliveries [docs https://code.claude.com/docs/en/hooks#configchange] [docs https://code.claude.com/docs/en/settings#when-edits-take-effect]. Open: whether that is worth another Sanduhr hook over a file watcher.
12. Should a "carry a lesson across" helper (the strip from identifier to category) ever live in Sanduhr, or does it belong only inside the seats? The default is no.

## Sources

- [docs https://code.claude.com/docs/en/settings] precedence, exceptions, local file location, edit timing, broken file recovery
- [docs https://code.claude.com/docs/en/settings-reference] keys and their Scope column
- [docs https://code.claude.com/docs/en/env-vars] `CLAUDE_CONFIG_DIR`, `CLAUDE_CODE_PLUGIN_DIRS`
- [docs https://code.claude.com/docs/en/mcp] scopes, collision order, CLI, connectors
- [docs https://code.claude.com/docs/en/plugins/loading], [docs https://code.claude.com/docs/en/plugins/cli-reference], [docs https://code.claude.com/docs/en/plugins/mods]
- [docs https://code.claude.com/docs/en/memory], [docs https://code.claude.com/docs/en/skills], [docs https://code.claude.com/docs/en/sub-agents], [docs https://code.claude.com/docs/en/output-styles], [docs https://code.claude.com/docs/en/hooks]
- [docs https://code.claude.com/docs/en/claude-directory] authored config versus app data, sweep rules
- [docs https://code.claude.com/docs/en/debug-your-config]
- [docs https://code.claude.com/docs/en/managed-settings] managed sources on macOS
- [local `claude --version` 2.1.293, `claude --help`, `claude plugin --help`, `claude mcp --help`, `claude doctor --help`, `claude purge --help`]
- [repo dotclaude-personal:.seat/README.md], `.seat/CLAUDE.md`, `.gitignore`, `.gitattributes`, `settings.json` (key names only), `.seat/apply/README.md`, `.seat/lib/jsonmerge.ps1`, `.seat/local-keys.json`, `.seat/seat.json`, `.seat/SHARED`, `.seat/docs/superpowers/specs/2026-09-29-same-surface-design.md`, `2026-09-29-same-surface-spike-results.md`, `2026-09-30-plugin-parity-design.md`
- [local work seat repo, `origin/main`, read by `git show` only] `.gitignore`, `.seat/lib/guards.ps1`, `.githooks/pre-commit`, `.seat/README.md`, the decision log, the routing script, the Mac installer, described as mechanisms
- [repo Sanduhr_f-r_Claude:mac/Sources/Sanduhr/Services/] `ClaudeCodeFolders.swift`, `JSONEdit.swift`, `IntegrationInstaller.swift`, `ModInventory.swift`, `ModSwitch.swift`, `ModCheck.swift`, `ModStatusEntries.swift`, `StatuslineCombine.swift`, `AccountData.swift`, `WatcherStore.swift`, `CCLogReader.swift`, `VaultIngester.swift`, `VaultStewardship.swift`; `mac/Sources/Sanduhr/Views/ModsSettings.swift`
- [repo Sanduhr_f-r_Claude:docs/superpowers/research/2026-10-05-claude-config-history-gaps.md], `2026-10-05-knowledge-carry-review.md`, `2026-10-06-claude-code-mods.md`

## Verification notes

Adversarial pass, 2026-10-07, against CLI 2.1.293, the docs pages listed under Sources (fetched as Markdown the same day), Sanduhr's Mac code on `feat/mac-batch-2`, the personal seat repo through `gh api`, and the work seat's `origin/main` read with `git show` and `jq keys` only. No values from any settings, credentials or `.claude.json` file were printed.

Confirmed as written: the stub `.claude.json` (423 bytes, no `oauthAccount`) and the live files beside it; the placement rule preferring the inner file; the CLI flags (`-s|--scope` and `--json` on plugin install, uninstall, enable, disable, update; `--json` on list and validate; `claude plugin details` with token cost; `claude mcp add|add-json|remove -s`; `claude purge` deleting a project's config entry); `mcp list` and `get` health-checking approved servers; the `failureCode` values; the strict-wins exceptions named; the scope rows for `pluginConfigs`, `appendPlugins`, `modelPicker`, `autoMode`; `mcpServers` ignored in `settings.json` and `permissions`, `hooks`, `env` misplaced in `.claude.json`; the four-hop import limit and the 200-line or 25 KB `MEMORY.md` load; `skills/synced/` being overwritten by sync; inline plugins replacing a marketplace plugin silently while `claude plugin list` still shows it enabled; `@synced` plugins loading without an `enabledPlugins` key; `/status` naming loaded files, not the source of each key; `cleanupPeriodDays` defaulting to 30 with minimum 1; ModCheck's runner (temp cwd, null stdin, `NO_COLOR`, 10 s, terminate then SIGKILL); the commit and atomic-write path; the backup that deletes itself only on a byte-identical return; ModInventory's `try?` read; ModOverrides as the `enabledPlugins` seed; the personal seat's `local-keys.json` holding `model` only, six plugin names enabled from two marketplaces, the spike results items 1, 6, 9, 11 and 12, `state.json` fields, `-DryRun`, `-Status` and the kill switch; the work seat's guard, pre-commit `--all`, routing script, decision log wording and live-versus-repo key drift (four repo keys missing live, two live-only keys, hook events differing both ways), by count only.

Corrected:

1. `failureCode` is documented in plugins/cli-reference, not plugins/loading.
2. "Keys with no writer" overstated: reworded to keys with no CLI writer and no `/config` row, tagged to the `/config` and edit-a-settings-file sections. `statusLine` dropped from the list.
3. `claude config list`: the absence of a `config` subcommand is now tagged to `claude --help`; the model-turn behavior is marked as the earlier lens's observation, not re-run.
4. `.claude.json` contents: "dozens of caches" replaced with the docs' list (sign-in, MCP, per-project state, global config keys) and the `backups/` rotation added. The seat-local `.claude.json` under `CLAUDE_CONFIG_DIR` is now tagged to a local check; the docs do not state it.
5. Secret mask: the merge driver's matcher is narrower (`api[_-]?key`, not `key`); the claim now says so, with line numbers.
6. Demo hiding: watchers hide in demo mode only; there is no separate screenshot mode. Citation moved to `Watchers.swift:330-332`, with `WatcherStore.swift:186-190` for the work test.
7. Wall switches: `deniedMcpServers` takes a `serverName` object, and the two `syncClaudeAi*` switches are ignored when `false` in a shared project file.
8. Precedence: added the command-line `--settings` level and pair-by-pair shell variables Sanduhr cannot see, the list-merge exceptions, and the full strict-wins set. `enabledPlugins` resolution is per plugin id.
9. Ignored keys: `defaultMode` covers `auto` too and local files too; `CLAUDE_CONFIG_DIR` is ignored in local `env` as well.
10. The `settings.local.json` docs tag no longer claims the docs "agree" on a repo-root rule for seat folders; a home-folder exception is added and marked unverified.
11. MCP: "six scopes" is now "six sources" in the docs' order, with managed servers on top and name-versus-endpoint matching stated.
12. Retention: the sweep runs in the background after a session starts; `history.jsonl` anchor fixed, with the HIPAA exception noted; `desktopSessionCleanupPeriodDays` scope noted.
13. Housekeeping: Claude Code's own `backups/.claude.json.backup.<timestamp>` files are not leftovers.
14. Seat commit format: the seats list paths with `+`, `~` and `-` prefixes, not counts; tagged to commit subjects.
15. Work seat memory reversal: both commits were on the same day, not on consecutive days; the "docs unchanged" part was not checked and has been removed.
16. Receipt decoder citation moved to `IntegrationInstaller.swift:117,1081-1084`; absolute-path citation moved to `:218-232,250-253`, with the hooks' `$HOME` form noted.
17. Strict JSON is tagged to the edit-a-settings-file section, not when-edits-take-effect.
18. Open questions 2, 5 and 11 are now partly or fully answered from the docs.

Still unverified: reading another process's `CLAUDE_CONFIG_DIR` on macOS (open question 4); whether GUI launches ever see the shell's `CLAUDE_CONFIG_DIR`; a claude.ai connectors endpoint for G11; whether `claude plugin enable` accepts `@inline` with `--scope`; whether `CLAUDE_CODE_PLUGIN_DIRS` is honored from a settings `env` block (the env-vars row does not exclude it, but ModSwitch's reliance on it is untested here); whether `claude plugin list --json` writes seat state; what created the stub `.claude.json`; the home-folder `settings.local.json` exception; the `Write(path)` rule finding (seat spike only, not checked against the permissions docs); the shared `state.json` schema across seats.

Public-repo scan: the work seat's tree names and keystone file were read to list its proper nouns (employer, internal apps and skills, project folders, hosts, people, the account path), and the spec was searched for each, for hostnames, domains other than code.claude.com, long numbers and secret-shaped strings. Work identifiers found: none. Secret values found: none.

## Checklist entry

- [ ] **71. Mods & Config: every seat's Claude Code config on one page, read first, edited with undo**
  Spec ref: `docs/mods-and-config-spec.md` (2026-10-07: the Mods page becomes "an easy place to manage Claude Code configuration across seats"); research `docs/superpowers/research/2026-10-05-claude-config-history-gaps.md` G1 to G11. Follows item 64.
  What to build, in slices that each ship: (0) the ground: the `.claude.json` placement rule prefers the live file (with `oauthAccount`, then newest) and Remove uses the receipt's file, `.claude-backup-*` skipped, "Add folder…", receipts decoded per record; (1) read-only page per seat with eight sections: Settings (effective value, source file, where it is honored, this machine only, dead-rule lint), Instructions (CLAUDE.md, rules and the `@` import tree), Extensions (agents, skills, commands, output styles, hooks, plugins with parity checks, and item 64's Mods), MCP servers (all six scopes, collisions, recommended route, names never values), History (the 30-day warning, size, growth), Housekeeping (leftovers, Sanduhr's own backups first, duplicate project keys), Seat (git state, `.seat/state.json`, held files, structural pushes waiting, the never-list as three buckets), Drift (seat against seat, live against repo, routing); (2) a `configEdit` receipt and edits with exact undo for `cleanupPeriodDays`, `desktopSessionCleanupPeriodDays`, `syncClaudeAiSkills`, `syncClaudeAiPlugins`, `disableClaudeAiConnectors` and `claudeMdExcludes` entries, backups in Application Support; (3) plugins and MCP through `claude plugin ... --json` and `claude mcp ...` with `CLAUDE_CONFIG_DIR` per seat, blocked while Claude Code runs; (4) the seat panel's dry run, wall audit and kill switch through the seat's own scripts; (5) drift actions, cleanup to the Trash, history archive into the vault.
  Wall: a seat marked work (account link or toggle) never feeds a copy, sync or export into a personal seat; cross-wall drift shows names only; labels are user-chosen and no tenant name from a seat's files is displayed; work rows hide in demo mode. Secrets: env values and secret-named keys masked everywhere and never editable; Sanduhr never writes `.claude.json` by hand, `.seat/`, ignore rules or hooks other than its own.
  Acceptance: slice 1 shows every section for every seat with no change to any seat (mtime check) and no secret value on screen, in logs or in `state.yaml`; raising retention clears the 30-day warning and Undo returns `settings.json` byte-identical; a duplicate plugin is disabled through the CLI and `claude plugin list --json` agrees; no control copies out of a work seat.
  Verify: swift-testing for the placement rule, precedence walker, scope badges, the mask, parity checks, never-list buckets, `state.json` parsing, the `configEdit` round trip under a concurrent write, the copy direction rule, and the CLI runner against a fake `claude` (argv, env, refusal while running, refusal of unknown subcommands); a smoke action per seat reporting counts; by hand on the owner's seats, checking the next seat sync commits only the edited key. First, the spec's open questions 1 to 4.
