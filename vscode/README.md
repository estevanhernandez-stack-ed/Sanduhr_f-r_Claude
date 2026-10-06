# Sanduhr Time

A local-first coding time tracker for VS Code that separates your time from Claude's.

<!-- screenshot: the Time panel, dark theme (add before a Marketplace listing) -->

WakaTime shows how long you coded. It does not tell you how much of that day Claude Code spent working while you steered. Sanduhr Time records both sides in one place, per project per day, and never counts the same minute twice.

## What it tracks

For every project, every day:

- **Your time.** Editor activity in a focused VS Code window (edits, saves, switching files, moving the cursor), plus the time you spent writing prompts to Claude Code.
- **Claude's time.** Read from Claude Code's own transcripts, in every config home on the machine (`CLAUDE_CONFIG_DIR` and each `~/.claude*` folder that has a `projects/` directory). Subagents count, and parallel sessions show up as agent time next to the split.
- **The split.** You only, Claude only, and both. The total is the union, so overlapping work counts once.
- **Lines.** Lines you changed in the editor, and lines Claude added or removed through its edit tools. Disk reloads of a file Claude just changed are not credited to you.
- **Languages.** Where your editor time went, by language.

A day starts when you install: history before that is not backfilled, so every recorded day has an honest split. Days follow your local calendar, and idle time ends a stretch after 15 minutes (a setting).

## What never leaves your machine

The tracker makes **zero network calls**. It has no runtime dependencies and opens no sockets. Everything it records stays in a folder you own.

- File paths are hashed with a per-install random salt before they touch disk. No spool file holds a path, a file name, file content, prompt text or transcript text.
- Project names live only in your local day files and in `aliases.json`.
- The one process it ever starts is `wakatime-cli`, if you have it, to read today's total for the comparison below. No shell is involved.

A separate, optional extension (the 626 Labs extension) can read the day totals through this extension's API and publish them to a 626 Labs dashboard. If you do not install it, nothing is ever sent. If you do, it sends day totals only, under neutral aliases (for example "Project Kestrel"). A real project name is sent only for a project bound to your dashboard, with streamer mode off and the project not masked. If the alias store cannot be read, it sends nothing.

## Where the data lives

```
~/.sanduhr/time/
  spool/    editor heartbeats, one file per window
  claude/   Claude events read from transcripts
  days/     one merged JSON file per day
  state/    install salt, read offsets, lock, caveats, WakaTime comparison
  aliases.json   project aliases, mask flags, streamer mode
```

Change the folder with the `sanduhrTime.dataDir` setting or the `SANDUHR_TIME_DIR` environment variable (the setting wins). Spool files older than 90 days are deleted; day files are kept. The formats are documented in [`docs/time-usage-spec.md`](docs/time-usage-spec.md), with a JSON Schema for day files in [`docs/time-day.schema.json`](docs/time-day.schema.json), so other tools can read the same files.

## The Time panel and status bar

The status bar shows today's total, for example `3h 12m`. Hover for you, Claude and both; click to open the Time panel. The panel shows today, a 7-day strip, and per project the stacked you/Claude/both bar, agent time, lines and top languages. It follows your color theme.

### Streamer mode and masking

Project names can be sensitive on a screen share. **Streamer mode** replaces every project name in the panel with a persistent alias. You can also mask or unmask a single project, or give it a new alias, from its row. Aliases come from a pool of neutral names and stay the same from day to day. Streamer mode and masks apply to publishing too, not only to the panel.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `sanduhrTime.idleMinutes` | `15` | Minutes of inactivity after which a stretch of editor time ends. |
| `sanduhrTime.compareWakaTime` | `true` | Show a side-by-side comparison with WakaTime when `wakatime-cli` is installed. |
| `sanduhrTime.dataDir` | empty | Data folder. Empty uses `SANDUHR_TIME_DIR`, then `~/.sanduhr/time`. |

## Commands

| Command | What it does |
|---|---|
| Sanduhr Time: Open Panel | Opens the Time panel. |
| Sanduhr Time: Toggle Streamer Mode | Masks or unmasks every project. |
| Sanduhr Time: Merge Now | Reads new transcript lines and rebuilds today's numbers now instead of waiting for the 5-minute tick. |
| Sanduhr Time: Compare With WakaTime Now | Runs the WakaTime comparison immediately. |
| Sanduhr Time: Reveal Data Folder | Opens the data folder in your file manager. |

## WakaTime side by side

If `wakatime-cli` is installed (in `~/.wakatime/` or on your PATH), the tracker asks it for today's total at most once an hour and logs the result next to its own in `state/wakatime-compare.jsonl`. The panel shows WakaTime's total against your editor time (you only plus both), with the difference. That is the fair comparison: WakaTime has no view of Claude's time. Without the CLI the comparison is hidden and nothing errors. Turn it off with `sanduhrTime.compareWakaTime`.

## Requirements

- VS Code 1.80 or newer, desktop.
- A local workspace. Remote, WSL and Codespaces windows are not supported: the extension runs on the machine where the transcripts live, and under a remote window it records nothing and says so.
- Claude Code, to have anything on Claude's side. Without it the tracker still records your time.

## For integrators

The extension returns a small API from `activate` (`getToday`, `getDays`, `onDayUpdated`, `maskState`, `projectKey`, `normalizeRemote`), and the files on disk are a documented contract: see [`docs/time-usage-spec.md`](docs/time-usage-spec.md). A consumer decides what to show; it should read `maskState()` live and fail closed.

## Build and test

```sh
npm ci
npx tsc --noEmit
npm run lint
npm test
node scripts/check-public.mjs
npm run package
npx @vscode/vsce@4.0.0 package --no-dependencies
```

The last command produces a `.vsix` you can install with `code --install-extension`.

## Not affiliated

Claude is a trademark of Anthropic. Sanduhr Time is not affiliated with or endorsed by Anthropic.
