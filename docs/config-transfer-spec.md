# Packs: share a mod, carry a Claude Code config

Spec, 2026-10-15. Follows item 71 (Mods & Config, `docs/mods-and-config-spec.md`) and item 75 (Mod Studio, `docs/mod-studio-spec.md`).

Evidence tags as in the Mods & Config spec: [docs <url>], [repo <path>], [local <observation>], [unverified].

## Verdict: one pack format, a mod first, then a whole folder, always reviewed before it lands

The owner, 2026-10-14, after keeping finish-line in the personal seat: "so is the next step to be able to share mods as well, because finish line is not currently available in work since we created in personal". Then, 2026-10-15: "import/export claude config planning. This is what we have been building towards."

Between seats on one Mac this already works: a kept mod lives in Sanduhr's folder and turns on per Claude Code folder (item 75, slice F2). What's missing is everything off this Mac, and everything that isn't a mod: a new machine, a friend, a fresh seat that should start with the skills, agents, plugins and MCP servers another one has.

Claude Code has no export, import or sync of a config folder; the documented pieces are marketplaces, settings files and `.mcp.json` [docs https://code.claude.com/docs/en/settings] [docs https://code.claude.com/docs/en/plugins/loading]. So a **pack** is a description plus the plain files that travel, never a copy of the folder. Exporting reads; importing replays through the routes Sanduhr already writes by: receipts for files and settings, Claude Code's own CLI for plugins and MCP servers.

A mod pack is the smallest config pack, so mods ship first and fix the format.

## Principles

- **A description, not a copy.** Caches, `installed_plugins.json`, `known_marketplaces.json` (absolute paths), `.claude.json` (sign-in, per-project trust, personal MCP), transcripts, history and memory never go in a pack [docs https://code.claude.com/docs/en/plugins/loading] [docs https://code.claude.com/docs/en/claude-directory]. Plugins travel as their marketplace source and name; installs are replayed.
- **No secret leaves.** `env` values and every secret-named key path (the item 71 mask: `env` plus `token|api[_-]?key|secret|password|credential|key`) are exported as names marked **needs a value**. MCP `env` and `headers` travel as `${VAR}` references, which Claude Code expands at load [docs https://code.claude.com/docs/en/mcp]. Plain files (CLAUDE.md, agents, skills, commands) are scanned before export; a hit blocks that file until it is unchecked or edited outside Sanduhr.
- **The wall holds.** No export control exists for a seat marked work (item 71: "nothing crosses from it", absent, not disabled). Importing into a work seat is allowed, as personal to work is, and asks once naming the seat. Demo mode hides work seats from both sheets.
- **Review before anything lands.** An imported mod becomes a proposal (item 75, slice G1): files, the validator's risk card, **Try in a Session…** after a passing check (slice G3), then Keep. An imported config opens as one side of Compare (item 71, slice 1d), with a checkbox per difference.
- **One undo per import.** Every write an import makes is a receipt the existing paths already know; the import groups them, and **Undo Import** reverses them in reverse order, each byte for byte where the file hasn't changed since.
- **Useful without Sanduhr.** A mod pack is also a one-plugin marketplace, so `claude plugin marketplace add <unzipped folder>` then `claude plugin install <name>@<pack>` works on any machine [docs https://code.claude.com/docs/en/plugins/marketplace-reference].

## The pack

A zip with the extension `.sanduhrpack` (a UTType Sanduhr declares, so a double-click or a drop on the widget opens Import).

```
sanduhr-pack.json            the manifest (below)
.claude-plugin/marketplace.json   mod packs: a one-plugin marketplace, source "./mod"
mod/                         mod packs: the plugin folder as kept, minus .claude-plugin/types
files/                       config packs: CLAUDE.md, agents/, skills/, commands/, output-styles/
mods/<name>/                 config packs: each kept mod chosen, as in a mod pack
```

`sanduhr-pack.json`, schema 1:

| Field | What |
|---|---|
| `schema` | 1 |
| `kind` | `mod` or `config` |
| `created_at`, `created_by` | a timestamp and `Sanduhr <version>`; no machine, user or seat name |
| `label` | the user's own words, default the mod's name or "Claude Code config" |
| `mod` | name, version, description, stamp (item 75's 12-hex content hash), and the validator's summary at export |
| `files` | path, bytes and SHA-256 per file; import refuses a pack whose bytes don't match |
| `settings` | config packs: chosen key paths with values, secret-named ones as `{"needs_value": true}` |
| `env` | config packs: names only, each `needs_value` |
| `plugins` | config packs: `{name, marketplace: {source}, scope}`; a local-path marketplace is listed as **this Mac only** and not replayed |
| `mcp` | config packs: `{name, transport, command, args, url, env, headers}` with `${VAR}` in place of every `env` and `headers` value |
| `kept_mods` | config packs: names and stamps of the mods under `mods/` |

Limits on import, checked before anything is unzipped to disk: 256 entries, 2 MB per file, 20 MB unpacked, relative paths only, no `..`, no links, no device files (the zip-slip and zip-bomb guards). A mod inside a pack is held to `propose_mod`'s rules on top (24 files, 64 KB each, 256 KB in all, `.ts .tsx .js .mjs .json .md`), so the review screen is the one that already exists [repo mac/Sources/Sanduhr/Services/ModProposal.swift]. Plugins with other file types (shell hooks, Python) travel as marketplace sources, not files, until open question 3 is settled.

## Sharing a mod

**Export…** on a kept mod's line (New Mod, From Claude, Kept) and on its Catalog card. A save panel, default name `<mod>-<version>.sanduhrpack`. The sheet before it shows what goes in: file count and size, the validator's summary, and that nothing from any folder's settings travels. Exporting writes the pack and nothing else.

**Import…** at the top of New Mod's From Claude, a drop on that sheet, or opening a `.sanduhrpack` of kind `mod`. The pack is checked (limits, hashes, the mod rules), staged read-only under `mods/.proposed/<id>/` as a proposal with origin **From a file** instead of From Claude, and lands under **Proposed** with the same review: files, the validator's card, Try in a Session…, Keep or Dismiss. The **Let agents propose mods** switch has nothing to do with it: the user opened the file. Keep makes it a kept mod, and turning it on in a folder is F2's consent, unchanged.

A pack whose mod has the name of a kept mod shows the difference: same stamp ("already kept"), or a new version beside the old, as Keep already does for a changed session mod.

## Carrying a config

### Export

**Export Config…** on By Seat, for a seat not marked work. A sheet lists what the seat has, grouped as Compare groups it, every row with a checkbox, nothing checked that isn't portable:

- **Instructions**: the seat's `CLAUDE.md` (off by default: it's often the most personal file in a seat).
- **Agents, skills, commands, output styles**: the seat's own files, by name.
- **Plugins**: each marketplace plugin, by name and marketplace source. Synced (`@synced`) plugins aren't listed: claude.ai brings them to any seat signed in to that account [docs https://code.claude.com/docs/en/plugins/loading].
- **Kept mods** that are on in the seat.
- **MCP servers** from the seat's `.claude.json`, read and never written, with every `env` and `headers` value turned into `${NAME}`.
- **Settings**: the keys Claude Code documents as shareable (`permissions`, `model`, `outputStyle`, `hooks`, `enabledPlugins`, `extraKnownMarketplaces`, the wall switches, `cleanupPeriodDays`), and `env` by name.

Never listed: Sanduhr's own entries (its statusline, MCP server and meters mod; the importing Sanduhr installs its own), `settings.local.json`, `.claude.json` itself, `projects/`, transcripts, memory, plugin caches and records, credentials.

**Paths.** A value that holds an absolute path under the home folder is written as `~/…` and turned back on import; any other absolute path (a hook command in `/opt`, a statusline script elsewhere) is listed as **this Mac only**, unchecked, and can't be checked. Plugin hook commands already use `${CLAUDE_PLUGIN_ROOT}` and travel [docs https://code.claude.com/docs/en/plugins/manifest-reference].

**The scan.** Before writing, every checked file is read for the mask's patterns and for long high-entropy strings. A hit shows the file and line number, never the match, and unchecks the file. There's no override in the sheet: edit the file, or leave it out.

### Import

Opening a config pack opens Mods & Config's **Compare** with the pack on the left as a read-only side and a seat chosen on the right (the first personal seat, or the one the pack came closest to). Compare already sorts into only-left, differs and matches [repo mac/Sources/Sanduhr/Services/ConfigCatalog.swift]. Each only-left or differs row gets a **Bring over** checkbox; matches have none.

**Import…** shows the plan before writing: per row, what will be written where and by which route.

| Row | Route | Undo |
|---|---|---|
| A file (agent, skill, command, output style, CLAUDE.md) | written into the seat with a new `fileAdd` receipt: path, the previous bytes or absent | deleted or restored, only if unchanged since |
| A settings key | item 71's `configEdit` receipt | byte-exact, as for every settings edit |
| `env` names | the user types each value into a field passed to the write and never kept; skipped names are not written | as settings |
| A kept mod | staged as a proposal first (above); once kept, turned on by F2's receipt | F2's Turn Off |
| A plugin | `claude plugin marketplace add <source>` then `claude plugin install <name>@<mkt> --scope user --json`, under the seat's `CLAUDE_CONFIG_DIR`, by item 71 slice 3's runner | `claude plugin uninstall`, and `marketplace remove` if the import added it |
| An MCP server | `claude mcp add-json -s user <name> '<json>'` by the same runner, blocked while a session runs in that seat [docs https://code.claude.com/docs/en/mcp] | `claude mcp remove -s user <name>` |

The import is one record in `imports.json`: the pack's hash, the seat, and its receipts in order. **Undo Import** walks them backwards and reports any it couldn't undo (a file changed since, a plugin the user updated) instead of forcing it.

## Slices

Each ships on its own and is useful without the next.

| Slice | What | Writes |
|---|---|---|
| A | Mod export: a kept mod to a `.sanduhrpack`, a one-plugin marketplace inside (built 2026-10-15: **Export…** in a kept mod's menu on New Mod, From Claude; `ditto` zips the tree; checked against Claude Code 2.1 in a scratch seat: `plugin validate` passes, `marketplace add` on the unzipped folder and `plugin install <mod>@<mod>-pack` install and enable it) | the pack file only |
| B | Mod import: a pack (or a plugin folder or zip dropped on From Claude) staged as a proposal, origin From a file; the UTType and double-click | staged proposal; the rest is item 75's |
| C | Config export: the sheet, path rewriting, the scan, the manifest | the pack file only |
| D | Config import, files and settings: the pack as a Compare side, Bring over, `fileAdd` and `configEdit` receipts, env values typed in, Undo Import | seat files and `settings.json`, by receipt |
| E | Config import, plugins and MCP servers through the CLI | through Claude Code's CLI only; needs item 71 slice 3's runner |
| F | A marketplace of your own: publish kept mods to a git repo the user names, so any machine adds it once with `claude plugin marketplace add owner/repo` | a git push the user confirms, to their repo |

A and B answer the owner's ask (finish-line on another machine or another person's Mac). C and D are the config; E waits on the CLI runner; F replaces passing files around once there is more than one mod worth sharing.

## Risks

- **A secret in a plain file.** CLAUDE.md, an agent or a command can hold a token pasted once. The scan blocks the file; the review lists every file by name before writing. The scan can miss a short or low-entropy secret, so the export sheet says what it checks.
- **Personal context in a pack.** CLAUDE.md and memory-like skills carry who the owner is. CLAUDE.md is off by default, memory never travels, and the sheet's first line says the pack is readable by anyone it's given to.
- **Crossing the wall by hand.** A work seat has no export, but its files can be copied by Finder. Sanduhr doesn't police the disk; it doesn't make the crossing easier. The seats' own guards stay the enforcement.
- **Running someone else's mod.** A mod from a file is a proposal: nothing runs before Try (after a passing check and a confirm) or Keep and Turn On (F2's consent).
- **A plugin install with side effects.** `plugin install` fetches from the network and writes the seat. It runs only from Import's plan, one plugin per line with its source shown, never while a session runs in that seat, and its result is shown per line.
- **A synced seat.** Importing into a seat that's a git clone becomes a commit at the seat's next sync. The plan says so, as item 71's edits do.
- **A hostile zip.** Entries are checked from the central directory before any byte is written: paths, links, sizes, counts; hashes after.

## Open questions

1. Should `env` values that are plainly not secrets (`CLAUDE_CODE_*` switches, `DISABLE_*` flags) travel with their values? The proposal is a short allowlist of Claude Code's documented variables, everything else by name.
2. Should a work seat be able to export to another work seat (a second work machine)? The owner's work seat already syncs through its own repo, so the default is no export from work at all.
3. Should a mod pack carry plugins that aren't mods (shell hooks, Python scripts)? `propose_mod`'s file rules don't allow them, and the validator's report covers mod modules, not shell hooks [unverified]. The default is to carry those as marketplace sources only.
4. Should Claude be able to propose a config change (an agent, a skill, a settings key) through the MCP server, as it proposes mods? It would reuse the switch and the review; not in these slices.
5. Should packs be signed? The default is no: trust comes from the review, and the hash only proves the pack wasn't damaged in transit.

## Acceptance

- finish-line exported from the personal seat's Mac and imported on another Mac (or a fresh user account) lands under Proposed, passes the check, runs with Try, keeps, and turns on in a folder there.
- The same pack unzipped works with `claude plugin marketplace add` and `claude plugin install` and no Sanduhr.
- A config pack of a personal seat with a token in an agent file refuses that file and names its line; the written pack contains no `env` value, no MCP header value and no absolute path outside `~`.
- Importing that pack into a scratch seat brings over the chosen agents, skills and settings keys, and Undo Import returns every touched file byte-identical.
- No control exports from a seat marked work; demo mode shows no work seat in either sheet.

## Verify

swift-testing: the manifest round trip; the zip guards (`..`, absolute paths, links, a 21 MB entry, 257 entries, a hash mismatch); path rewriting both ways; the scan against fixtures with and without secrets; the plan builder for each row kind; `fileAdd` and Undo Import byte-exact under a concurrent change; the export direction rule. The CLI rows against item 71's fake `claude`. By hand: finish-line between the owner's Mac and a second user account; a config pack into a scratch seat under a temporary `CLAUDE_CONFIG_DIR`.

## Checklist entry

Item 76 in `docs/checklist.md`.
