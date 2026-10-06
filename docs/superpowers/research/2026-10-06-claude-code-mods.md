# Claude Code mods: what they are, what they can draw, and how Sanduhr manages them

Research pass, 2026-10-06. Five independent lenses (official docs, local ground truth, text effects, statusline combining, management UX), merged. Every factual claim carries its evidence tag. Nothing here was installed or configured; settings files were read for key names only.

## Verdict: mods are plugins with a hooks module, and Sanduhr already owns most of a manager

A mod is an ordinary Claude Code plugin whose behaviour lives in one ES module exporting `register(on, options)`. Mods landed in 2.1.287 ("Added Claude Mods") and are on by default from 2.1.287 in the terminal and 2.1.286 in the Desktop app [docs https://code.claude.com/docs/en/plugins/mods/overview] [web https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md]. The public docs speak of early access in the past tense, but the 2.1.288 typings header still reads "EARLY ACCESS: this surface may change between releases without notice", and every release since has changed mod APIs [local claude-code.d.ts:4] [web CHANGELOG.md 2.1.288-2.1.292]. The CLI already has every verb a manager needs except a UI: `claude plugin validate --json` is a capability report that lists what a mod hooks and calls without running it [docs https://code.claude.com/docs/en/plugins/mods/admin#review-what-a-mod-can-do], `claude plugin test` runs a mod's tests, `claude plugin list --json` lists installed plugins (marketplace and `@synced` alike) [local `claude plugin --help`; `claude plugin list --help`]. Sanduhr already has the other half: byte-exact receipts for `env.CLAUDE_CODE_PLUGIN_DIRS` [repo mac/Sources/Sanduhr/Services/IntegrationInstaller.swift:271-303, 526-561, 720-740], content-addressed stamps with an atomic `current` link [repo mac/Sources/Sanduhr/Services/IntegrationScripts.swift], and a working propose, validate, approve loop for themes and desk messages [repo mac/integrations/sanduhr_mcp.py:1306-1363].

Recommended order:

1. **Fix the Windows statusline clobber first.** Windows overwrites a user's statusLine without naming it in the consent dialog, and Remove deletes the key instead of restoring the user's line; the only copy left is the timestamped `settings.json.sanduhr-backup-<stamp>` file it writes beside settings [repo windows-dotnet/src/Sanduhr.Core/StatuslineInstaller.cs:108-160, 197-205] [repo windows-dotnet/src/Sanduhr.App/Views/StatuslineConsentDialog.xaml.cs:49-54]. That is a data-loss bug in practice (recoverable only by hand), not a feature gap.
2. **Item 63, Combine on Mac.** A third button on the existing "other statusline" sheet, a `--chain-b64` flag on `sanduhr_statusline.py`, unchanged undo. Small, contained, and the work seat on this Mac is the live case [local jq keys ~/.claude/settings.json].
3. **Item 64, slice 1: a read-only Mods page** (inventory plus validate report). Zero risk, immediately useful for the "hard to manage" problem.
4. **Item 64, slices 2-4:** on/off and remove through receipts, then Adopt from dev-mods, then `propose_mod`.
5. **Text effects ride along as presets**, first inside sanduhr-meters (gradient bar, warning pulse, spinner suffix), later as a data-driven effects mod that reuses the Desk grammar.

## A mod is a plugin with a hooks module, loaded four ways

**Files.** Required: `.claude-plugin/plugin.json`, `hooks/hooks.json` naming the module under `"modules"` (an array holding one path), and the module itself (`.ts/.tsx/.js/.mjs/...`). `types/index.d.ts`, named by the manifest's `types` key, is needed only when the mod uses `$.state` or adds a `$` namespace. `*.test.ts(x)` are optional. Settings-style shell/HTTP hooks can sit in the same `hooks.json` under `"hooks"` [docs https://code.claude.com/docs/en/plugins/mods/reference#files] [local plugin-authoring reference.md]. `claude plugin init` scaffolds a skills-dir plugin of command hooks, not a mod; a mod is "three files written directly" [local `claude plugin --help`; reference.md line 9].

**Sanduhr's mod** has four files: manifest with three `userConfig` pickers (`bars`, `style`, `label`), a one-module `hooks.json`, `register.tsx` hooking only `session.start` and `ui.render{component:'AbovePrompt'}`, and `types/index.d.ts`, which is the mod's own 23-line state contract (`MetersTier`, `MetersSnapshot`, a `PluginState` augmentation), not the API [repo mac/integrations/mods/sanduhr-meters/.claude-plugin/plugin.json:6-28; hooks/register.tsx:129,137,168; types/index.d.ts:1-23]. Its tests pass 27/27 [local `claude plugin test`].

**Loading routes.** The docs-lens counts four routes; the local-lens counts three. They agree once the dev-mods folder is counted as its own route:

| Route | Settings key | Hot reload | Evidence |
|---|---|---|---|
| Marketplace install, copied to `~/.claude/plugins/cache/<mkt>/<plugin>/<version>/` | `enabledPlugins["<name>@<mkt>"]` | no | [docs https://code.claude.com/docs/en/plugins/loading] |
| `@inline`: `--plugin-dir`, `--plugin-url`, `CLAUDE_CODE_PLUGIN_DIRS`, SDK `plugins` | `enabledPlugins["<name>@inline"]: false` in any settings file disables (and stops it shadowing); options in `pluginConfigs["<name>@inline"]` | yes, in place (interactive sessions watch the folder) | [docs https://code.claude.com/docs/en/plugins/loading#find-plugins-on-disk] [docs https://code.claude.com/docs/en/plugins/mods/reference#settings-and-environment-variables] [local reference.md:68] |
| `@skills-dir`: `~/.claude/skills/<name>/` or project `.claude/skills/<name>/` with a manifest (project copy only after workspace trust) | `enabledPlugins["<name>@skills-dir"]` | yes, interactive sessions watch it | [docs https://code.claude.com/docs/en/plugins/loading] [local reference.md:68] |
| Per-session `<config>/dev-mods/<sessionId>/`, after the person answers "Enable hot reloading for this session?" | none | yes | [docs https://code.claude.com/docs/en/plugins/mods/create#ask-claude-for-a-mod] |

`CLAUDE_CODE_PLUGIN_DIRS` takes absolute paths separated by `:` (`;` on Windows), read from the process env or the `env` block of user `~/.claude/settings.json`, never project settings. A cloned repo therefore cannot enable a mod through it. `CLAUDE_CODE_PLUGIN_DIR_WATCH=1` makes long-running non-interactive sessions (SDK, Desktop) hot-reload; a one-shot `claude -p` always loads fresh [docs https://code.claude.com/docs/en/plugins/mods/reference#settings-and-environment-variables] [local reference.md:68]. The "never project settings" rule is stated in the bundled skill reference; the public docs table lists only the environment and user `~/.claude/settings.json` as sources.

**On/off and kill switches.** `claude plugin enable|disable <plugin> [-s user|project|local] [--json]` writes `enabledPlugins`, which merges key by key across sources, so a project `true` beats a user `false`. Settings or disk changes reach a running session only through `/reload-plugins` or a new session [docs https://code.claude.com/docs/en/plugins/loading#find-where-a-plugin-is-enabled] [local `claude plugin enable --help`; `claude plugin disable --help`, which also has `-a, --all`]. Global switches: `--safe-mode` (one session), `disableAllHooks: true` (which also kills the custom statusline), managed `allowManagedModsOnly` (an option on the built-in `sec-default` guard) / `allowManagedHooksOnly`. `disableAllHooks`, `--bare` and `--safe-mode` do not stop built-in mods [docs https://code.claude.com/docs/en/plugins/mods/overview#turn-mods-on-or-off] [docs https://code.claude.com/docs/en/plugins/mods/reference#settings-and-environment-variables].

**Trust model.** Mods are not sandboxed. They run with the user's permissions and can approve tool calls. Everything outside the module goes through `$` (no DOM, no Node, no `require`, and a module holding `import()` does not load), which is why `validate` can enumerate calls statically; a mod that uses `$` in a way `validate` can't read is refused [local claude-code.d.ts:7-16] [docs https://code.claude.com/docs/en/plugins/mods/admin#review-what-a-mod-can-do]. The one surface a mod cannot change is the permission prompt; a process a mod starts runs outside the Bash sandbox [docs https://code.claude.com/docs/en/plugins/mods/overview#what-a-mod-can-reach].

**Versioning.** Manifest `version` is a free string, not semver-checked; setting it pins users to the cached copy until it changes. `claude plugin tag` makes `{name}--v{version}` tags [local `claude plugin --help`]. Reserved names (error): `claude`, `anthropic`, `anthropics`, `claude-code`, `claude-mods`, and any name starting `claude-`, `anthropic-`, `anthropics-` or `cc-plugin-`. Only `claude plugin init` and `claude plugin tag` enforce this; Claude Code still installs and loads such a plugin [docs https://code.claude.com/docs/en/plugins/manifest-reference#version].

**Samples.** Public built-in sources: `anthropics/claude-code/mods/{agents-md, diff, sec-default, telemetry}`; `mods/types/` beside them holds only `claude-code.d.ts` (written by 2.1.277, older than the local build), not a mod. Built-ins without public source include `cc-plugin-plugin-authoring` and `cc-plugin-you-should-know` (off by default) [local `gh api repos/anthropics/claude-code/contents/mods`] [docs https://code.claude.com/docs/en/plugins/mods/overview#how-a-mod-works]. Unsupported samples: `anthropics/claude-code-playground/claude-code/mods/{token-weather, blast-radius, replay-theater}`, with token-weather being a context forecast above the prompt, the closest cousin to sanduhr-meters [local `gh api repos/anthropics/claude-code/contents/mods`] [docs https://code.claude.com/docs/en/plugins/mods/overview#try-a-sample-mod].

### Contradictions and skew between lenses

- **Dev-mods path.** Docs say `~/.claude/dev-mods/<sessionId>/`; this session's skill named `~/.claude-personal/dev-mods/<sessionId>/` [local plugin-authoring SKILL "WHERE TO WRITE IT"]. Resolution: the folder follows `CLAUDE_CONFIG_DIR`; this session (config dir `~/.claude-personal`) has its own `~/.claude-personal/dev-mods/<sessionId>/` and no `~/.claude/dev-mods/` exists [local `echo $CLAUDE_CONFIG_DIR`; `ls -d ~/.claude*/dev-mods`]. The docs path is the default seat. The folder is purged after `cleanupPeriodDays` [docs https://code.claude.com/docs/en/plugins/mods/create#use-the-mod-in-other-sessions].
- **Type version.** The binary is 2.1.292 but the only API typings on disk say "Written by Claude Code 2.1.288" and lack `ThemeKey`/`Color` (added 2.1.290). They sit in a per-process `/private/tmp` folder [local `claude --version`; claude-code.d.ts:1-8]. The package ships no mod types, only `sdk-tools.d.ts` [local /opt/homebrew/lib/node_modules/@anthropic-ai/claude-code/package.json]. The GitHub copy (`mods/types/claude-code.d.ts`) is older still, written by 2.1.277 [web https://raw.githubusercontent.com/anthropics/claude-code/main/mods/types/claude-code.d.ts]. Trust the copy the engine writes beside a mod it loads from `--plugin-dir` or dev-mods, at `.claude-plugin/types/claude-code/index.d.ts` [docs https://code.claude.com/docs/en/plugins/mods/reference] [docs https://code.claude.com/docs/en/plugins/mods/create#get-the-types-for-your-build]. Line numbers cited below are from the 2.1.288 file.
- **Enable/disable for folder mods.** The settings route is documented: `"enabledPlugins": {"<name>@inline": false}` in any settings file turns a folder mod off [docs https://code.claude.com/docs/en/plugins/loading#find-plugins-on-disk]. The CLI help says only "Enable a disabled plugin" / "Disable an enabled plugin" with `-s user|project|local`, and the docs show `claude plugin disable <name>@synced` working [local `claude plugin disable --help`] [docs https://code.claude.com/docs/en/plugins/loading#control-which-synced-plugins-load]. Whether the CLI accepts `@inline` ids is [unverified]. Sanduhr should toggle its own mods through the `CLAUDE_CODE_PLUGIN_DIRS` list, which it already controls by receipt.
- **`$.ui.status` shape.** Resolved by the docs: the line "starts with `⚠` and the mod's name, as in `⚠ my-mod: checks: 3 passing`", stays until changed [docs https://code.claude.com/docs/en/plugins/mods/api]. It takes one plain string; ANSI handling is [unverified]. The warning-glyph prefix makes it a poor home for a calm meter.
- **"Timeout under the 300 ms debounce."** The UX-lens proposed a chain timeout under 300 ms. The 300 ms figure is a debounce, not a timeout [docs https://code.claude.com/docs/en/statusline]. The statusline-lens's 1.5 s budget is the one to use.
- **Python startup cost** (30-50 ms) is [unverified] on this Mac.

## The `$` API, compactly

From the 2.1.288 declarations [local claude-code.d.ts] unless tagged otherwise.

| Noun / event | What it gives a mod | Notes |
|---|---|---|
| `on('ui.render', {component})` | Rewrite or replace an engine site: `UserMessage`, `AssistantMessage`, `ToolUse`, `ToolResult`, `ToolGroup`, `CommandOutput`, `AskUserQuestion`, `Spinner`, `SessionMode`, `PromptHint`, `AbovePrompt`, `Pane`; terminal-only `ToolProgress`, `TurnDuration`, `InfoNotice` | No `StatusLine` member in `RenderComponent` [local d.ts:8712] [docs https://code.claude.com/docs/en/plugins/mods/reference#render-sites] |
| `$.ui.open / status / toast / log / invalidate / blit` | Pane, one pinned line, 4 s toast, dim transcript line, re-render, raster repaint | `invalidate` 10/s, or 30/s in the terminal for the visible pane, expanded band and hint line [docs https://code.claude.com/docs/en/plugins/mods/interface#how-often-a-site-can-redraw] |
| `$.session.usage()` / `session.measure` | `{startedAt, context, rateLimits[{kind, percentUsed, resetsAt}], cost}` | "The status line's figures": the account's rate-limit windows as this session last saw them (so only as fresh as its last API response), empty off a subscription [local d.ts:2620-2642, 11005-11020] [docs https://code.claude.com/docs/en/plugins/mods/reference#mods-api-methods] |
| `$.settings.read({source})` | merged or per-source settings (`user|project|local|flag|policy`), every key unfiltered including `env`; never `~/.claude.json` | [local d.ts:3348-3372, 11127] |
| `$.process.run(argv, {cwd, env, stdin, timeoutMs})` / `spawn` | host command, no shell, 30 s default | [local d.ts:3290-3307, 7538-7600] |
| `$.fs.read/write/list/stat/exists`, `$.http.fetch` | any path (read up to 4 MiB), any reachable host | full user authority [local d.ts ~3015-3041, 3261-3282] |
| `$.env.get(name)` | env vars, string literals only, so `validate` lists every name | [local d.ts env.get] |
| `$.state` / `$.store` | host-held session state (owner writes, others read) / key-value store shared by every session on the machine, 4 MiB | `$.state` survives a hot reload where module variables don't; `$.store` outlives the session [local reference.md:68, 88] [docs https://code.claude.com/docs/en/plugins/mods/reference#limits] |
| `$.clock.every/now` | timers, at least 1 ms; hot reload drops them | [local d.ts:3245-3256] [local reference.md:68] |
| `$.config.list/set`, `userConfig` | `/config` rows; change reloads module with new `options` | `claude plugin configure --values-stdin` sets without UI [local `claude plugin --help`] |
| `tool.call/check`, `prompt.compose/submit`, `session.append`, `agent.spawn`, `telemetry.*` | gate or rewrite tool calls, edit system prompt, inject messages | the high-risk set; `validate` reports `gatingHooks` |
| `plugin.register`, `engine.create`, hooks on `$` calls | `{refuse: reason}` for a joining module; add (or, outside the `user` tier, withhold) a `$` namespace; intercept any `$` call made by mods that run later | no `$` noun lists, enables or disables other mods [local d.ts:4157-4166] [docs https://code.claude.com/docs/en/plugins/mods/reference#other-mods] |

Consequence: no API manages mods, so a manager inside a mod would have to hand-edit settings files through `$.fs.write` (which is not atomic). It belongs in Sanduhr (editing settings by receipt) or the CLI [docs https://code.claude.com/docs/en/plugins/mods/reference#mods-api-methods].

## Text effects: mods animate, statuslines print

The ceiling differs by more than an order of magnitude. A mod draws a typed element tree in-process: `ui.render` redraws up to 30/s for the visible pane, band and hint line, and `Client` / `Raster` repaint on the surface's frame clock (the types' `blit` example ticks every 33 ms) [docs https://code.claude.com/docs/en/plugins/mods/reference#limits] [local d.ts:1407-1412, 2193]. A higher figure such as 60 fps is [unverified]. A statusline is one process per trigger, at best once a second via `refreshInterval` [docs https://code.claude.com/docs/en/statusline].

**Mod toolbox.** `Text` props: `color`, `backgroundColor`, `dimColor`, `bold`, `italic`, `underline`, `strikethrough`, `inverse`, `wrap` (7 modes), `hover`. No blink, no gradient or shimmer prop. A prop outside the allowlist rejects the whole tree and the engine draws its own [local d.ts:11865-11887] [docs https://code.claude.com/docs/en/plugins/mods/interface]. Colors are "a theme key or a raw color" (the `ThemeKey` type arrived in 2.1.290), so hex works; sanduhr-meters already ships `#f87171` [repo hooks/register.tsx]. `Box` adds borders, absolute positioning and `hover: {display:'flex'}` for hover-reveal cards [local d.ts:704-830]. `Raster` (terminal only): 1-512 x 1-256 cells, base64 of u32 `[codePoint, fg, bg]`, width-1 BMP glyphs only, palette-rounded to 1024 color pairs (`0x2e7d32` draws as `#337733`) [local d.ts:8600-8650] [docs https://code.claude.com/docs/en/plugins/mods/gallery]. `Client`: a surface module on the drawing thread with `surface.every(ms)`, `setState`, `onKey`, `onPointer`; a `setState` on three renders in a row with no key, pointer, tick, press or props between is treated as a render loop and unmounts it, so timer-driven animation through `surface.every` is fine [local d.ts:1256-1264, 1386-1412]. `Image` (kitty graphics, kitty and Ghostty only, `alt` elsewhere; this Mac's `TERM_PROGRAM` is vscode, so it falls back) and desktop `Svg` with SMIL and `:hover` [local d.ts:4991-5060, 11578-11610] [local `echo $TERM_PROGRAM`]. `Link` accepts only `https:` or `http://localhost`, so no `sanduhr://` from a mod [local d.ts:5330-5346].

Limits: 100,000 drawn characters per tree and a 10 s hook budget (50 ms for `prompt.edit`) [docs https://code.claude.com/docs/en/plugins/mods/reference#limits]; a `Client`'s tree is bounded at 20,000 nodes, 32 deep, 100,000 serialized characters, and each Client call at one second, or it unmounts [local d.ts:1233-1234, 1258-1260]. Drive animation from `await $.clock.now()` phase, not tick counts; timers run late [web https://gist.github.com/ruvnet/a485e930b148185197fc53fd38b429ea].

**Statusline toolbox.** Every stdout line is a row; ANSI SGR and OSC 8 links render (`FORCE_HYPERLINK=1` overrides detection); read width from `COLUMNS`/`LINES`, never `tput cols`; empty output or non-zero exit blanks the row; escape-heavy multi-line output is "more prone to rendering issues" [docs https://code.claude.com/docs/en/statusline]. This Mac has `COLORTERM=truecolor` and JetBrainsMono NL Nerd Font installed [local `echo $COLORTERM`; `ls ~/Library/Fonts`]. Italic, blink and inverse passthrough is [unverified]; treat blink as unsupported.

**Width traps.** Block, box-drawing and `◆` are East Asian Width "A" (two columns in ambiguous-wide terminals); `⚠` plus VS16 becomes a two-column emoji; `🔥` is "W". Raster refuses non-width-1 glyphs outright [local python3 unicodedata.east_asian_width]. Nerd Font overdraw is [unverified] per terminal.

### Catalog

| Effect | How | Cost | Example |
|---|---|---|---|
| Spinner suffix / word | `ui.render{component:'Spinner'}` returns `next({...e, props:{...e.props, suffix:' · 5h 42%'}})`; `mode` tells thinking from tool-use | cheapest; engine keeps animating | [local d.ts:9426-9475] [docs https://code.claude.com/docs/en/plugins/mods/interface] |
| Per-cell gradient bar | one `<Text color={lerp(a,b,i/n)}>` per cell or run, eighth-block `▏▎▍▌▋▊▉` leading edge | full tree validation per redraw | already the shape of meters' runs [repo hooks/register.tsx meterRow] |
| Warning pulse | `$.clock.every(100)` alternating two reds by `clock.now()` phase for 2-3 s, then cancel | within 10/s invalidate budget, then idle | design [unverified in practice] |
| Shimmer / wave / rainbow / typewriter | `Client` surface module, `surface.every(ms)` + `setState` | drawing thread, no hook round trip | [local d.ts:1372-1430] |
| Sparkline or heat strip | `Raster` of braille or `▀` half-blocks, repainted by `$.ui.blit` (types' example: every 33 ms) | one base64 string per frame, no render pass; terminal only | [local d.ts:2180-2196, 8617-8640] |
| Glitch-at-threshold | mutate cells to random blocks above 55%, inverse at 70-85% | statusline: one process per tick | thereprocase/claude-statusline [web https://github.com/thereprocase/claude-statusline] |
| Hover card | `Box display:'none' hover={{display:'flex'}} position:'absolute' top={-2}` | no hook runs on hover | [local d.ts:704-830 @example] |
| Animated SVG | `Svg isInteractive` with SMIL | desktop only, up to 131072 chars | [local d.ts:11578-11610] |
| Truecolor gradient line | `\e[38;2;R;G;Bm` per glyph, fall back to `\e[38;5;Nm` without truecolor | process per trigger | kcchien/claude-code-statusline [web https://github.com/kcchien/claude-code-statusline] |
| Powerline caps, multi-stop gradients | Nerd Font `U+E0B0/E0B2`, hex stops | `bunx -y ccstatusline@latest` 633 ms vs 207 ms without `@latest` (202 ms pinned), Windows, warm cache | ccstatusline [web https://github.com/sirmalloc/ccstatusline README] |
| Clickable label | OSC 8 `\e]8;;URL\e\\text\e]8;;\e\\` | none; terminal decides clickability | [docs https://code.claude.com/docs/en/statusline] |
| Full-width cameo | braille helix or rockets at `refreshInterval: 1` | ~1 fps (README: "repaint ~once a second so the ... cameos can actually move") | SethWoodbury/beautiful_fun_claude [web https://github.com/SethWoodbury/beautiful_fun_claude] |

Other community pieces named in search results but not opened: gravicity-archive/claude-statusline (pulse), meros/claude-usage-statusline (sparklines) [unverified: repos exist, features not reviewed]. A third, maverickmunoz9/statusline, returns 404 [local `gh api repos/maverickmunoz9/statusline`] and is dropped. The "cool text effects" the owner found are probably community mods built on `Client`/`Raster` (the ruvnet field-guide gist, chanlito/claude-mods) [unverified: not reviewed against the official lens].

**Sanduhr already has an effects grammar.** The Desk's `{ink:#hex,...}` (2+ colors is a gradient), `{glow}`, `{size:x}`, `{write}`, `{shimmer}` is enforced in both `MessageMarkup.parseStrict` and the MCP's `parse_effects` [repo mac/Sources/Sanduhr/Desk/MessageEffects.swift] [repo mac/integrations/sanduhr_mcp.py:1147 `parse_effects`]. One vocabulary can drive the Desk, the band (Text and Raster) and the statusline (ANSI truecolor). That turns most "make it shimmer" requests into data an agent proposes and Sanduhr lints, not code it reviews.

## Item 63: Combine wraps the user's statusline inside Sanduhr's command

There is exactly one `statusLine`, and no native compose. A plugin cannot ship one: plugin `settings` honors only `agent` and `subagentStatusLine` [docs https://code.claude.com/docs/en/plugins/manifest-reference]. So Combine is a wrapper command. Prior art that converges on the same shape: herdr-agent-usage [web https://github.com/levi-qiao/herdr-agent-usage], an `ach statusline` chaining proposal, now closed [web https://github.com/sblattj/agentic-coding-harness/issues/61] [local `gh api repos/sblattj/agentic-coding-harness/issues/61`], and ccstatusline's Custom Command widget going the other direction [web https://raw.githubusercontent.com/sirmalloc/ccstatusline/main/docs/USAGE.md].

**Contract Sanduhr must honor.** `{type:'command', command, padding?, refreshInterval? (>=1), hideVimModeIndicator?}`. Triggers: session start, each assistant message, `/compact`, mode and vim changes, `refreshInterval`, `resets_at` / `expires_at` passing. Debounce 300 ms; a new trigger cancels the in-flight run. Stdin JSON carries `rate_limits.five_hour/seven_day.used_percentage` and `resets_at`, `context_window.*`, `model.*`, `workspace.*` and more [docs https://code.claude.com/docs/en/statusline]. Same trust gates as hooks (blank until workspace trust; `disableAllHooks` hides it), which the wrapper inherits for free. Project-level `statusLine` shadows the user one Sanduhr writes [docs https://code.claude.com/docs/en/settings].

**What exists today.** `sanduhr_statusline.py` reads only `snapshot.json`, ignores stdin, prints one plain line, always exits 0 [repo mac/integrations/sanduhr_statusline.py]. `isOurStatusline` treats any command containing `; & | $(` backtick or newline as the user's [repo IntegrationInstaller.swift:260-266]. `.other` status, `needsReplaceConsent(existing:)` and the "Replace and Install" sheet already exist [repo IntegrationInstaller.swift:323-329,439] [repo mac/Sources/Sanduhr/Views/IntegrationsSettings.swift:366,423-442]. `receipt.previous` holds the user's member bytes and Remove restores them exactly [repo IntegrationInstaller.swift:61-82, 483-493, 684-688]. Gap: replace writes only `type`+`command`, dropping the user's `padding`, `refreshInterval`, `hideVimModeIndicator` until Remove [repo IntegrationInstaller.swift:178-188, 495].

**Design.**

- **Command shape.** `<python> <.../current/sanduhr_statusline.py> --chain-b64 <base64(previous command)> --join line|same`. Base64 needs no escaping, contains none of the metacharacters, works per seat with no sidecar file, and does not depend on `CLAUDE_CONFIG_DIR` being exported to the script, which is undocumented [unverified]. `isOurStatusline` widens to accept exactly that flag grammar and nothing else.
- **Siblings carried forward.** The new value copies `padding`, `refreshInterval`, `hideVimModeIndicator` from the previous object (herdr gets the same effect by editing the existing object in place, keeping its other keys, and keeps the original in a sidecar backup file rather than in the command) [web https://github.com/levi-qiao/herdr-agent-usage src/configure/statusline.rs].
- **Runner.** Read stdin once as bytes. `Popen(['/bin/sh','-c',cmd], stdin=PIPE, stdout=PIPE, stderr=DEVNULL, start_new_session=True)` with the same bytes and inherited env (keeps `COLUMNS`/`LINES`). Budget 1.5 s via `communicate(timeout=)`, `os.killpg(pgid, SIGKILL)` on timeout, a SIGTERM/SIGINT handler that kills the group so Claude Code's cancel does not orphan the user's script, 64 KiB capture cap. Set `SANDUHR_CHAIN_DEPTH=1` in the child and skip chaining if already set.
- **Ordering.** `--join line` (default): the user's rows first, Sanduhr's segment as its own last row. Safe with powerline and TUI lines that pad to full width. `--join same`: append `\x1b[0m │ <segment>` to the user's last line, so their open SGR or OSC 8 cannot bleed. Auto-fall back to `line` when visible width (CSI and OSC stripped, wide chars counted as 2) plus segment exceeds `COLUMNS − padding − ~20` (room for right-side notifications).
- **Failure policy, the opposite of herdr's.** herdr prints nothing on timeout, and on a non-zero child exit prints the child's stdout without its segment and exits with the child's code [web https://github.com/levi-qiao/herdr-agent-usage src/configure/claude.rs `run_statusline_hook`]. Sanduhr: user command fails or times out, still print Sanduhr's segment; child exits non-zero with stdout, keep that stdout; always exit 0; reason to stderr (reaches only `claude --debug`). Sanduhr's segment empty, print the user's output untouched. When the snapshot is dead, fall back on stdin `rate_limits` and mark it (`5h 42%*`).
- **Install-time unwrap.** If the previous command is itself Sanduhr's (plain or combined, any seat, any version), decode and chain the innermost foreign command.
- **Undo.** Unchanged. `previous` stays the exact member bytes; Remove is the existing `JSONEdit.restore`. Add optional `IntegrationReceipt.mode: "replace"|"combine"` so old receipts still decode. "Outdated" refresh rebuilds the command from mode plus decoded payload. A hand-edited command no longer matching the grammar reads as the user's and is never touched (current invariant).
- **UI.** The `.other` sheet becomes [Combine] (default) / [Replace] / [Cancel], with a preview that runs the combined command once against the docs' mock JSON and renders the ANSI as `AttributedString`, both join modes. Row label: "Combined with your statusline".
- **Zero-touch alternatives offered alongside.** sanduhr-meters drawing into `PromptHint.tail` (dim text after the hint line, terminal only) [local d.ts:9541-9567], and a "Copy as Custom Command widget" button for ccstatusline users. Neither edits `statusLine`.
- **Pin with tests.** Combine then Remove leaves settings.json byte-identical including siblings; the flag grammar is exact; a 5 s sleeper child yields Sanduhr's segment within ~1.6 s with no orphan (`pgrep` empty); unreset ANSI gets `\x1b[0m` on same-line join; non-zero child output is kept; nested Sanduhr previous is unwrapped.

**Windows note.** Fix parity before Combine: `StatuslineInstaller.Register` sets `root["statusLine"]` unconditionally, and `Deregister` removes the key only when it is Sanduhr's, so a user's line replaced at install is never restored; the only trace is the `settings.json.sanduhr-backup-<stamp>` copy taken before each write. The consent dialog mentions that backup but never that an existing statusline will be replaced [repo windows-dotnet/src/Sanduhr.Core/StatuslineInstaller.cs:108-160, 197-205] [repo windows-dotnet/src/Sanduhr.App/Views/StatuslineConsentDialog.xaml:52; .xaml.cs:49-54]. Port the receipt model (foreign detect, consent, stored previous, restore). Combine then embeds `-ChainB64 <b64>` in the `powershell -NoProfile -ExecutionPolicy Bypass -File "<ScriptPath>"` command [repo StatuslineInstaller.cs:96-99]. Claude Code runs statusLines through Git Bash when present, else PowerShell, and eats unquoted backslashes under Git Bash [docs https://code.claude.com/docs/en/statusline]. The runner must use the same shell Claude Code would (`bash.exe -c` else `powershell -NoProfile -Command`), `WaitForExit(1500)` and `Kill($true)` for the tree. Which Git Bash Claude Code picks is [unverified].

## Item 64: a Mods page that turns mockups into managed versions

The "mockups until live, then hard to manage" problem has a concrete source: agent-written mods land in per-session `dev-mods/<sessionId>/` folders that are scattered and purged after `cleanupPeriodDays` [docs https://code.claude.com/docs/en/plugins/mods/create#use-the-mod-in-other-sessions] [local ls ~/.claude-personal/dev-mods/]. The page gives them one home with versions.

**Current state on this Mac.** No mod is installed in any seat: no settings.json has an `env` key, so `CLAUDE_CODE_PLUGIN_DIRS` is unset; `installs.json` receipts cover only `mcp` and `hooks` for `~/.claude`; no `pluginConfigs` anywhere [local jq key names; installs.json kinds]. The work seat's settings have 25 `enabledPlugins` keys across `@claude-plugins-official` and `@vibe-plugins`; the personal seat's settings have one `@claude-plugins-official` key, but `claude plugin list --json` there returns 10 plugins: that one plus 9 `@synced` from claude.ai [local jq key names; `claude plugin list --json` under `CLAUDE_CONFIG_DIR=~/.claude-personal`]. So settings keys alone undercount; the inventory must call the CLI. The installer knows exactly one mod: `IntegrationKind.meters`, recognized by suffix `/mods/sanduhr-meters` inside `/Sanduhr/integrations/` (`isOurModEntry`), with no per-mod toggle and no `pluginConfigs` writing [repo IntegrationInstaller.swift:4-38, 271-303, 526-561].

**Seat note.** The work seat (`~/.claude`) belongs to an employer tenant, and the meters manifest's author is 626Labs LLC. The manager should default to the personal seat and ask before installing into a work seat.

### Page anatomy

- **Inventory across Claude folders.** For each seat (`~/.claude`, `~/.claude-personal`, any other `~/.claude*` with a settings.json, resolved through `CLAUDE_CONFIG_DIR`): `claude plugin list --json` (keys `enabled, id, installPath, installedAt, lastUpdated, projectEnabled, scope, version`) [local `claude plugin list --json | jq '.[0]|keys'`], plus the `CLAUDE_CODE_PLUGIN_DIRS` list, plus `skills/*/.claude-plugin/plugin.json`, plus `dev-mods/*/`. Classify a plugin as a mod when its `hooks.json` has `"modules"`. Whether `plugin list` includes `@inline` and `@skills-dir` mods is [unverified], hence the direct scans. Rows: name, version, origin badge ("Bundled", "From Claude, <date>", "Adopted from dev session", "Marketplace", read-only for anything Sanduhr does not own), surfaces drawn, risk tier, per-seat toggles, last test result, version menu.
- **Preview, tier 1 (static, automatic).** Parse `claude plugin validate --json --strict <dir>` into a permissions sheet: hooks with matchers, every `$` call (transitive, "via" helpers), env names read and written, state reads and writes, `gatingHooks`. It runs no code [local `claude plugin validate --help`] [docs https://code.claude.com/docs/en/plugins/mods/admin#review-what-a-mod-can-do]. For sanduhr-meters it reports `session.start`, `ui.render{component=AbovePrompt}`, `$.clock.every`, `$.env.get`, `$.fs.read`, `$.state.*`, `$.store.*`, `$.ui.toast`, env reads `APPDATA, HOME, OS, SANDUHR_SNAPSHOT` [local `claude plugin validate --json mac/integrations/mods/sanduhr-meters`]. Color-code: green `ui.*`, `clock`, `state`, `store`, `fs.read` of Sanduhr's folder; amber other `fs.read`, env reads, model calls; red `process.*`, `http.fetch`, `fs.write`, `config.set`, `tool.call/check`, `prompt.compose/submit`, `session.append`, `agent.spawn`, `telemetry.*`, env names matching `/KEY|TOKEN|SECRET|PASSWORD/`.
- **Preview, tier 2 (runs code, explicit press).** A Sanduhr-owned trusted `preview.test.tsx` that `$.ui.mount`s `AbovePrompt`, `Pane`, `Spinner` against fixture snapshots and serializes the Text/Box tree (or Raster cells) for a SwiftUI fake-terminal render. Raster `cells` is plain portable data a native view can draw exactly. Label the button "Preview (runs the mod's code)". The test docs say every `$` call in a test "needs a stub that answers in Claude Code's place" (an unstubbed `$.clock.now()` fails with `no implementation for clock.now`), which suggests unstubbed `$.process` / `$.http` fail rather than reach the host; confirming that for those two nouns is [unverified] [docs https://code.claude.com/docs/en/plugins/mods/test#look-up-what-a-stub-returns]. Optional third button: "Try in a session", which opens Terminal with `claude --plugin-dir <stamp>` under a throwaway `CLAUDE_CONFIG_DIR`.
- **Test.** `claude plugin test <dir>`: pass/fail counts, exit 1 on failure, "hooks modules are turned off" when mods cannot load, 5 s per-test default [local `claude plugin test --help`] [docs https://code.claude.com/docs/en/plugins/mods/test]. The CLI says test files run "each file in a child of this binary, in an environment like the one the mod's hooks run in" [local `claude plugin test --help`]; the separate `claude plugin eval` command warns its sandboxing "is not a guarantee" [local `claude plugin --help`], and nothing documents a sandbox for `test` at all. So Test is a user-triggered button, never automatic for unapproved code.
- **On/off.** Generalize `IntegrationKind.meters` into a `ModEntry` list. Enable splices the mod's `current` path into `env.CLAUDE_CODE_PLUGIN_DIRS` via JSONEdit with a receipt (`createdKey`, `createdParent`, `previous`, `emptyInner`); Disable removes only that entry and keeps the folders. Widen `isOurModEntry` to `/Sanduhr/mods/<name>/current` under a new root, never touching a user's own entries. Global "Disable all mods" removes every Sanduhr entry in every seat, by receipt. Settings changes reach a running session only at `/reload-plugins` or the next session [docs https://code.claude.com/docs/en/plugins/loading]; whether `/reload-plugins` re-reads `CLAUDE_CODE_PLUGIN_DIRS` from the settings `env` block is [unverified]. Say "takes effect in new sessions" until verified.
- **Remove with exact undo.** Three levels: Disable (receipt-exact, folders kept), Remove (Disable, then move `mods/<name>/` to `mods/.trash/<name>-<date>/` for 30 days), Purge. Undo of Remove restores the folder and re-applies the stored receipt. Pattern matches Rainmeter's Backup folder and Este's own mod-manager-builder holding folder [web https://docs.rainmeter.net/manual/installing-skins/] [local ~/.claude-personal/skills/synced/.../mod-manager-builder/SKILL.md].
- **Versions and updates.** Reuse IntegrationScripts' content addressing: `mods/<name>/<stamp>/` (first 12 hex of SHA-256 over names and bytes) plus a `current` link swapped by one `rename(2)`, but keep old stamps. That yields history, pin and one-step rollback. Agent mods must live outside `integrations/`, because `refresh()` deletes every unused stamp there [repo IntegrationScripts.swift]. Exclude engine-written `.claude-plugin/types/` from hashing, as `isShipped` already does. Updates follow Chrome's rule: diff the new validate inventory against the approved one; any new amber or red capability arrives disabled pending re-approval; an identical inventory gets a file diff and one-click Update [web https://developer.chrome.com/docs/extensions/develop/concepts/permission-warnings]. VS Code's Disable/Uninstall split, "Install Another Version" and bisect-by-disable-all are the UX references [web https://code.visualstudio.com/docs/configure/extensions/extension-marketplace].
- **Adopt from dev-mods.** Scan every seat's `dev-mods/*/` for `.claude-plugin/plugin.json`, offer "Keep this mod", route through the same staging and validation as `propose_mod`, copy into a stamp, leave the originals.

### The `propose_mod` agent flow

The engine's only consent gate for agent mods is the "Enable hot reloading for this session?" prompt, and it applies to the dev-mods folder only [docs https://code.claude.com/docs/en/plugins/mods/create#ask-claude-for-a-mod]. Sanduhr's gate covers everything else, copying the theme handoff: MCP checks shape, writes `<kind>-request.json` atomically at 0600, polls `<kind>-result.json` up to 10 s, returns `queued` / `app_not_responding` on silence; the app picks it up through `HandoffWatch`, `HandoffFiles.take` reads and deletes, decodes again as the authority [repo sanduhr_mcp.py:1306-1363, 1651-1711] [repo mac/Sources/Sanduhr/Services/HandoffFiles.swift] [repo ThemeProposalHandoff.swift].

1. **What the agent sends.** `{name: ^[a-z0-9][a-z0-9-]{0,39}$, version, summary (<=200 chars), files: [{path, text}], surfaces?: ['terminal','desktop'], replaces_version?: stamp}`. Files inline, never a folder path, keeping the existing "No tool takes a path" rule [repo sanduhr_mcp.py docstring]: a path invites a prompt-injected agent to point at arbitrary disk, and invites a check-then-swap race. Caps (a design choice): 256 KB total (the `HandoffFiles.take` default), 24 files, 64 KB each. Extensions `.ts .tsx .js .mjs .json .d.ts .md`. Relative paths, no `..`, no symlinks, no binaries. Reserved names refused. Manifest built by the server when missing, refused when its `name` disagrees. Version stays a free string, matching the manifest rule.
2. **Server-side validation.** Shape only, then write `mod-request.json`. The server never writes into `mods/`.
3. **App staging, automatic and static.** Write files to `staging/<stamp>/` at 0444, persisted to disk (a code review outlives a session, unlike the in-memory theme proposal). Check collisions against built-ins, `sanduhr-meters` and every seat's `enabledPlugins` keys (`$.state` is keyed by plugin name, so a collision lets one mod observe another). Run `validate --json --strict`, then `tsc -p`. Answer `pending_review` with `request_id` and `stamp` at once, because validation can outrun the 10 s wait.
4. **Status for the agent.** New read tool `get_mod_status(request_id | name)` returns `pending_review | validating | invalid(findings) | approved | enabled | dismissed` plus inventory and findings, so the agent iterates the way it does on ThemeLint findings.
5. **Preview.** The risk card, then the optional user-pressed Test and Preview.
6. **Approval.** Red capabilities require a typed confirm. Approve moves the stamp to `mods/<name>/<stamp>/` and swaps `current`. Enabling is a separate toggle per seat, receipt-backed.

### Security risks and handling

| Risk | Handling |
|---|---|
| A mod has full user authority: process, network, any file, env secrets, tool-call rewriting, system-prompt edits [local d.ts fs/http/process/env/config.set; SKILL.md table] | Static risk card before anything runs; red tier needs typed confirm; master "Allow mods from Claude" switch off by default, as Obsidian's Restricted Mode [web https://obsidian.md/help/Extending+Obsidian/Plugin+security] |
| Persistent prompt injection via `prompt.compose`, silent Bash rewriting via `tool.call` | Both are red; `gatingHooks` surfaced by name; the permission prompt itself stays untouchable by mods [docs https://code.claude.com/docs/en/plugins/mods/overview#what-a-mod-can-reach] |
| Running tests executes unapproved code | Tier split: static checks automatic, `claude plugin test` and Preview only on explicit press, labeled. An OS sandbox (`sandbox-exec`) around them is an open option |
| Path-based proposals and check-then-use races | Inline files only; staged copies are 0444, stamps hashed |
| Trust creep from the theme flow | No "let Claude change mods directly" mode, unlike `themeClaudeDirect` / `messageClaudeDirect` [repo ThemeProposalHandoff.swift directKey]; a new proposal never silently replaces one under review |
| Hot reload makes any write into a loaded folder go live [local reference.md] | Read-only stamps (0444 files, 0555 dirs), hash recheck on every launch, drift flagged "modified outside Sanduhr". Open: whether the engine can still write `.claude-plugin/types/`; a writable `types` subfolder inside a read-only stamp is the fallback |
| Escalation through updates | Chrome rule: new capability, disabled until re-approved, with a capability diff |
| Name collision with an installed plugin | Refuse at staging |
| A cloned repo enabling mods | `CLAUDE_CODE_PLUGIN_DIRS` is never read from project settings [local reference.md] |
| Effects requests forcing code review | Offer `propose_effects` as data in the Desk grammar, lint-checked like themes, rendered by one Sanduhr-owned effects mod |

### Shippable slices

1. **Read-only Mods page.** Inventory across seats, validate-based risk card, origin badges. No writes. Also surfaces dev-mods that are about to be purged.
2. **Manage Sanduhr's own mods.** `ModEntry` list, new `mods/` root with stamps and `current`, per-seat on/off, Disable all, Remove-to-trash with undo, Test button. Migrate sanduhr-meters into it.
3. **Adopt.** Keep-this-mod from dev-mods through staging and validation.
4. **`propose_mod` + `get_mod_status`.** Staging, persisted proposals, master switch off by default, typed confirm for red, re-approval on escalation.
5. **Previews and effects.** Trusted `preview.test.tsx` renderer, then the data-driven effects mod and `propose_effects`. Windows mirroring decided separately, since the Windows MCP is network-free by contract and pinned by `TrustBoundaryTests` [repo CLAUDE.md].

Text effects that can ship inside slice 2 without the agent flow: per-cell gradient fill with eighth-block edge, warning pulse on threshold crossing, Spinner suffix `· 5h 42%`, and a `$.session.usage()` / `session.measure` fallback so the band never goes dark when `snapshot.json` is stale. Gate Raster and Image on `e.surface === 'terminal'` with a Text fallback for Desktop, and fold every effect the same way meters folds `full` to `compact` under 64 columns or `maxRows` [repo hooks/register.tsx fullFits].

## The owner's own mods are the working prior art

The owner has run six mods of his own since 2026-10-02, kept in a private config repo and loaded through `CLAUDE_CODE_PLUGIN_DIRS`, each with its own tests. They show the surfaces and the effects in production, not in docs [local, owner's config repo, read 2026-10-06]:

| Mod (kind) | Surface | What it proves |
|---|---|---|
| Now playing | band above the prompt, slash commands | per-song look picked by a fast model, cached per song, an instant default while it answers; per-character gradient title in a Unicode letter style; a progress bar with a glow pulse; synced lyrics with a shine on each new line; a play/pause/next pill that hides and returns on hover |
| A project card | pane, slash command | panes with a banner image read from disk, Refresh and Open buttons |
| Agent messages waiting | band, toast | a running clock per item; a toast when a reply lands |
| A decision nudger | system prompt, toasts | mods can add a standing instruction and nudge after commits |
| Home-lab health | status line entry, toasts | polling every minute, a toast on change, a slash command to poll now |
| A media-server pip | status line entry | a glyph only while a service answers, silent otherwise |

**The effects, from the now-playing mod's code:**

- **Letter styles:** ten Unicode "fonts" from the Mathematical Alphanumeric blocks (bold, italic, bold italic, sans, mono, double-struck, script, fraktur) plus small caps, with the Letterlike-block "holes" mapped by hand (script `ℬ ℰ ℱ ℋ`, double-struck `ℂ ℍ ℕ`, fraktur `ℭ ℌ ℑ`). Pure string mapping: it works in a band, a statusline, a toast, the Desk and the notch alike.
- **Per-character gradients:** one color per character, interpolated across two to four stops; eight named palettes (synthwave, sunset, ocean, aurora, ember, bubblegum, toxic, gold) seeded by a hash of the song, so a song always opens with the same look.
- **A light sweep:** brighten toward white across a three-character window, brightest at its center; the bar pulses every 4 s (each sweep about 1.1 s), the title shines once when a song or its look lands (1.4 s), a new lyric line shines once (0.9 s). Repaint at 80 ms only while a light moves, else once a second: the cost discipline the Limits table asks for.
- **The model picks, the code draws:** the model answers one JSON object (`font`, two to four `#rrggbb` colors, a mood of three words at most); anything the band can't draw is refused and the default stays. This is Sanduhr's propose-and-validate pattern in miniature.

**His gotchas, for the Mods page's help and the `propose_mod` checks:** a band hook calls `next(e)` and draws above the result so several mods share the band; a Box hover reveal is refused on a Box already shown (attach it only while hidden); `$.http.fetch` returns text only, so binary files go through `$.process.run` and `Image` reads them from disk; test fixtures must be `.ts`, and stubs for engine events answer `{ value: ... }`; `$` may only be passed to functions declared at the top of the module; only `settings.json` is read for `env` and `pluginConfigs` (not `settings.local.json`), and something has stripped `pluginConfigs` before, so a mod's defaults belong in its own `plugin.json` (`userConfig.<field>.default`).

## Candidate item 65: agent-styled text across Sanduhr

The letter styles, gradients and sweep are data, so they extend the Desk effects grammar Claude already writes (`{ink}`, `{glow}`, `{size}`, `{write}`, `{shimmer}`) without any new code path for the agent:

1. **Letter styles as tags:** `{font:script}`, `{font:fraktur}`, `{font:smallcaps}` and the rest, drawn by the same mapping, so Desk lines, the notch and the statusline share one vocabulary. Glyph coverage depends on the font: EsteFont 26 has no math-alphanumeric glyphs, so a styled line falls back to the system font for those characters (decide: allow, or draw the style only in the system font).
2. **A per-song look for now playing** on the notch and the Desk line: a gradient and a letter style per track, cached per song, a hash-seeded default until a look arrives. The look comes from Claude through the MCP server (a `propose_now_playing_looks` tool or a field on the theme proposal), approved like any suggestion; Sanduhr never calls a model itself, keeping the app's only network traffic claude.ai.
3. **Themes with text personality:** a theme may carry an ink gradient and a title letter style, so "make me a synthwave theme" covers the text too; the theme lint gains checks for contrast per gradient stop.
4. **The sweep** joins `{shimmer}` as `{sweep}` (the three-character light, once or on a period), with the same rest rules: no motion with Reduce Motion, nothing while the Desk is covered.

Windows parity: the same grammar and lookups, drawn in WPF (port plan Wave 3).

## Open questions and what to verify by hand

1. Run sanduhr-meters once under `claude --plugin-dir` in a fresh 2.1.292 session to lay that build's `.claude-plugin/types/` and diff against the 2.1.288 typings.
2. Does `claude plugin list --json` include `@inline`, `@skills-dir` and dev-mods mods? (It does include `@synced`.) Does `claude plugin enable/disable` accept `<name>@inline`? (The settings key `enabledPlugins["<name>@inline"]: false` is documented.)
3. Does the plugin-dir watch follow a `current` symlink retarget, or pin the resolved path?
4. Does `/reload-plugins` pick up a `CLAUDE_CODE_PLUGIN_DIRS` change in the settings `env` block, or only a new session? (Docs: settings changes need `/reload-plugins` or a new session in general.)
5. Does a read-only stamp stop the engine writing `.claude-plugin/types/`, and does that block loading or only lose editor types?
6. Inside `claude plugin test`, do unstubbed `$.process` / `$.http` fail (as the docs imply for every unstubbed call) or reach the real host?
7. How does Claude Code cancel an in-flight statusline: SIGTERM to the process, the group, or SIGKILL? Any hidden per-run timeout?
8. Does the statusline renderer pass SGR 3, 5, 7 and `48;2` backgrounds? Are `COLORTERM`, `NO_COLOR`, `CLAUDE_CONFIG_DIR` exported to the script? Test with a scratch `statusLine` in a throwaway config dir.
9. Does `$.ui.status` render or strip ANSI? (The `⚠ <mod>:` prefix is documented as always present.)
10. Which `ThemeKey` values exist in 2.1.292 (theme keys follow light/dark better than hex)? `Text color` accepts a theme key or a raw color; the 2.1.288 typings predate `ThemeKey`.
11. Does the Desktop app honor `CLAUDE_CODE_PLUGIN_DIRS` from `~/.claude/settings.json` when `CLAUDE_CONFIG_DIR` points at another seat?
12. Combine defaults: own-line join by default? Set `refreshInterval: 60` when the user had none (it changes how often their script runs)? Warn when a project-level statusLine shadows the user one?
13. Can a mod reconstruct the statusLine stdin JSON well enough to run the user's line inside the band (`$.settings.read` + `$.process.run`)? Plausible per the API, undocumented as a pattern, and not recommended over the wrapper.
14. Windows: which Git Bash does Claude Code pick?
15. Does VS Code's integrated terminal make statusline OSC 8 clickable without `FORCE_HYPERLINK=1`?
16. Anything official on multi-statusline composition? Nothing through 2.1.292 [web https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md].

## Sources

Official docs:
- https://code.claude.com/docs/llms.txt
- https://code.claude.com/docs/en/plugins/mods/overview
- https://code.claude.com/docs/en/plugins/mods/create
- https://code.claude.com/docs/en/plugins/mods/reference
- https://code.claude.com/docs/en/plugins/mods/interface
- https://code.claude.com/docs/en/plugins/mods/gallery
- https://code.claude.com/docs/en/plugins/mods/api
- https://code.claude.com/docs/en/plugins/mods/test
- https://code.claude.com/docs/en/plugins/mods/admin
- https://code.claude.com/docs/en/plugins/manifest-reference
- https://code.claude.com/docs/en/plugins/loading
- https://code.claude.com/docs/en/statusline
- https://code.claude.com/docs/en/settings
- https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md
- https://github.com/anthropics/claude-code/tree/main/mods
- https://github.com/anthropics/claude-code-playground/tree/main/claude-code/mods

Community and prior art:
- https://gist.github.com/ruvnet/a485e930b148185197fc53fd38b429ea
- https://github.com/sirmalloc/ccstatusline and https://raw.githubusercontent.com/sirmalloc/ccstatusline/main/docs/USAGE.md
- https://github.com/kcchien/claude-code-statusline
- https://github.com/Owloops/claude-powerline
- https://github.com/thereprocase/claude-statusline
- https://github.com/SethWoodbury/beautiful_fun_claude
- https://github.com/gravicity-archive/claude-statusline
- https://github.com/meros/claude-usage-statusline
- https://github.com/levi-qiao/herdr-agent-usage
- https://github.com/sblattj/agentic-coding-harness/issues/61
- https://obsidian.md/help/Extending+Obsidian/Plugin+security
- https://developer.chrome.com/docs/extensions/develop/concepts/permission-warnings
- https://code.visualstudio.com/docs/configure/extensions/extension-marketplace
- https://developers.raycast.com/basics/prepare-an-extension-for-store
- https://docs.brew.sh/FAQ
- https://docs.rainmeter.net/manual/installing-skins/
- https://github.com/felixhageloh/uebersicht

Local:
- `claude --version` (2.1.292); `claude plugin --help`, `test --help`, `validate --help`, `list --help`, `enable --help`; `claude plugin list --json`; `claude plugin validate --json` and `claude plugin test` on sanduhr-meters
- /private/tmp/claude-2092010161/bundled-skills/2.1.288/5e3053143e4334cc5b3fa9b661a394fd/plugin-authoring/reference.md and types/claude-code.d.ts
- /opt/homebrew/lib/node_modules/@anthropic-ai/claude-code/package.json
- Key names only: ~/.claude/settings.json, ~/.claude/settings.local.json, ~/.claude-personal/settings.json; kinds and folders only: ~/Library/Application Support/Sanduhr/integrations/installs.json
- ~/.claude-personal/skills/synced/.../mod-manager-builder/SKILL.md

Repo:
- mac/integrations/mods/sanduhr-meters/ (plugin.json, hooks/hooks.json, hooks/register.tsx, hooks/meters.ts, hooks/*.test.ts(x), types/index.d.ts, tsconfig.json, .gitignore)
- mac/integrations/sanduhr_statusline.py, mac/integrations/sanduhr_mcp.py, mac/integrations/install.sh
- mac/Sources/Sanduhr/Services/IntegrationInstaller.swift, IntegrationScripts.swift, HandoffFiles.swift, ThemeProposal.swift, ThemeProposalHandoff.swift
- mac/Sources/Sanduhr/Views/IntegrationsSettings.swift
- mac/Sources/Sanduhr/Desk/MessageEffects.swift, MessageProposal.swift, DeskMessageHandoff.swift
- mac/README.md (lines 225-310)
- windows-dotnet/src/Sanduhr.Core/StatuslineInstaller.cs, StatuslineScript.cs; windows-dotnet/src/Sanduhr.App/Views/StatuslineConsentDialog.xaml

## Verification notes

Adversarial pass, 2026-10-06, against the docs pages fetched as Markdown (`code.claude.com/docs/en/<page>.md`), the CHANGELOG, the 2.1.288 typings and `reference.md`, `claude` 2.1.292 help output, `claude plugin validate --json` and `claude plugin test` on sanduhr-meters (27 pass, 0 fail), the repo, and GitHub. No config was changed.

Corrected:
- "Still labeled early access": the public docs use the past tense; only the 2.1.288 typings header still says EARLY ACCESS. Retagged to that source.
- `claude plugin list --json` lists installed plugins, `@synced` included, not just marketplace ones. The personal seat shows 10 plugins (1 official + 9 `@synced`), not "swift-lsp only".
- `@inline` disable key lives under `enabledPlugins`; skills-dir and `CLAUDE_CODE_PLUGIN_DIRS` folders are watched in interactive sessions.
- Kill-switch citation moved from loading-page line numbers to the overview and reference pages. `allowManagedModsOnly` is an option on the built-in guard.
- Reserved plugin names: the full list adds `claude`, `anthropic`, `anthropics` and the `anthropics-` / `cc-plugin-` prefixes, enforced only by `init` and `tag`.
- `mods/types` on GitHub is a typings folder (written by 2.1.277), not a built-in mod.
- `$.session.usage()` rate limits are the account's windows as this session last saw them, not "not account-wide".
- `plugin.register` is not the only cross-plugin lever: `engine.create` and hooks on `$` calls exist too. A mod could manage mods only by raw `$.fs.write` to settings, so "cannot live inside a mod" was softened.
- Mod frame rate: "~60 fps" is unsourced. The documented ceilings are 30 redraws/s and a frame clock (33 ms example).
- The 20,000-node / 32-deep / 1 s limits are `Client` bounds from the typings, not the docs Limits table.
- The `Client` render-loop rule names key, pointer, tick, press or props as resets, so timer animation is safe.
- The `claude plugin test` warning was misattributed. "Not a guarantee" belongs to `claude plugin eval`.
- `$.ui.status` `⚠ <mod>:` prefix: confirmed by the API docs; the contradiction is resolved.
- ccstatusline timings: 633 ms is `@latest`, 207 ms unpinned, 202 ms pinned.
- The ach chaining issue is closed, not open. maverickmunoz9/statusline returns 404 and was removed.
- herdr: the timeout and child-failure behaviour is now described exactly. Siblings carry over because herdr edits the existing object in place.
- Windows: the user's line is overwritten at Register (with a timestamped backup), and Deregister removes only Sanduhr's entry. The original said Deregister destroyed the user's line.
- The state/clock rows and a few repo line references were updated to the current lines.

Still unverified: whether the CLI accepts `@inline` ids; whether `plugin list` shows `@inline`, `@skills-dir` or dev-mods mods; whether `/reload-plugins` re-reads `CLAUDE_CODE_PLUGIN_DIRS` from settings `env`; whether unstubbed `$.process` / `$.http` in tests reach the host; ANSI in `$.ui.status`; SGR 3/5/7 passthrough in the statusline; whether `CLAUDE_CONFIG_DIR` / `COLORTERM` are exported to the statusline; how a statusline is cancelled (signal, group); which Git Bash Windows picks; Python startup cost; whether a symlink retarget is followed by the folder watch; read-only stamps and engine-written types; mod frame rates above 30/s; community features in gravicity-archive, meros and chanlito repos; the ruvnet gist's late-timer advice (present in the gist, untested). Design items (Combine runner, `propose_mod`, risk tiers, caps) are proposals, not facts, and were not checked.

Confidence: high on the mods file layout, settings keys, env vars, CLI verbs, statusline contract, render sites, `Text`/`Raster` props and the Sanduhr repo facts. Medium on hot-reload and enable/disable edge cases and on anything sourced only from the 2.1.288 typings, which are four releases behind the binary.
