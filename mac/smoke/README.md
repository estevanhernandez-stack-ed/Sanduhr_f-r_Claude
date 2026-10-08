# Mac smoke tools

See and check the running Mac app without clicking through `docs/mac-smoke-test.md` by hand:
window screenshots, the UI tree as YAML, a live watch page, and scenarios that drive the app and
assert on what it shows. Design: [docs/mac-smoke-automation.md](../../docs/mac-smoke-automation.md).
Development tooling only; the hooks do nothing in a build where they are off (the default).

Ruby 2.6 (the system Ruby), stdlib only. Run from anywhere: `mac/smoke/smoke <command>`.

## Quick start

```
cd mac && ./build.sh --debug
# Make sure sanduhr:// links reach this build, not an older copy in /Applications:
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f Sanduhr.app
open Sanduhr.app

smoke/smoke enable             # defaults write com.626labs.sanduhr debugHooks -bool true
smoke/smoke snap               # prints the folder: renders, tree.yaml, state.yaml, screen-*.png
smoke/smoke tree settings      # the Settings window's UI tree (widget, desk, notch, camera, settings, whats-new, sheet)
smoke/smoke state              # Desk, notch, widget, Settings, meters, alerts, menu, version
smoke/smoke do settings notch  # one action, waits for it
smoke/smoke watch 5            # live log + out/watch/index.html refreshing every 5 s; Ctrl-C stops
smoke/smoke run                # every scenario in scenarios/; or name files
smoke/smoke view               # HTML gallery of the newest snapshot or run
smoke/smoke disable            # hooks off again
```

`SANDUHR_DEBUG_HOOKS=1` in the app's environment turns the hooks on without the default
(`SANDUHR_DEBUG_HOOKS=1 Sanduhr.app/Contents/MacOS/Sanduhr`; set it in the CLI's environment too,
so its "hooks are off" check stands down). `SMOKE_TIMEOUT=<seconds>` changes how long the CLI
waits for an answer (default 10).

## How it works

The CLI opens `sanduhr://debug/snapshot?dir=…` or `sanduhr://debug/action?name=…&arg=…&dir=…`
with `open -g` (no activation). The app writes its files into `dir`, then `done` last (or
`error` with the message). In-app code: `mac/Sources/Sanduhr/Debug/`.

- **Renders** (`widget.png`, `desk.png`, `notch.png`, `settings.png`, `sheet.png`) are drawn in
  process; no permission needed. Vibrancy and other compositor effects may draw flat.
- **Screen captures** (`screen-<window>.png`) come from `screencapture -l <window id>`, one per
  Sanduhr window, using the ids in `tree.yaml`. They need Screen Recording permission for the
  terminal; without it the CLI warns and the renders still work.
- **tree.yaml** holds, per window, two kinds of nodes:
  - **`OCRText`**: every line of text actually drawn in the window, read from its render with
    Vision (in process, no permission). `label` is the text, `frame` its place on screen. This is
    what scenarios match on. It also catches text that is clipped or drawn outside its window.
    Settings, in the system font, reads cleanly; the widget and Desk draw in the user's font,
    where recognition is loose ("SessioN (5 hr)"), so match those with case-insensitive
    patterns (`/(?i)^sess?io/`) or check `state.yaml` instead.
  - **Accessibility nodes** (`role`, `subrole`, `label`, `value`, `enabled`, `frame`,
    `children`). SwiftUI builds this tree only for a real assistive client (VoiceOver, or a
    process with Accessibility permission), so in a plain smoke run each window shows just its
    `AXHostingView`. With VoiceOver on, the full tree appears, switch values included.
  Switch on/off values are therefore not in the text nodes: check them in `state.yaml`. The
  `text` match field checks a node's label and value.
- **state.yaml**: `desk_enabled`, `desk_running`, `layout`, `desk_pieces` (item 59: each piece the Desk
  draws, in the layout's order: `piece`, the widget word; `anchor`, `tl`, `tc`, `tr`, `ml`, `mr`, `bl`,
  `bc` or `br`; `order`, its place in that anchor's stack, 0 at the top; `scale`, its size, 0.6 to 1.6),
  `desk_arrange` (item 60: `active`, Arrange mode is on; `changed`, its edit differs from the saved layout;
  `working`, the layout string being edited, null outside Arrange mode; `click_through`, `whole` while the
  Desk window takes clicks over its whole frame, `drawn` when only what is drawn takes them; `bar_visible`,
  the floating panel with Cancel and Done is on screen),
  `notch`, `has_notch`,
  `notch_left`, `notch_right`, `notch_strip` (what each place on the island shows, a
  `NotchContent` raw value such as `meetingOrTime`, `meters` or `nothing`; the default when unset),
  `camera_in_use` (an app is using a camera; only watched while the camera light switch is on),
  `camera_light` (the camera light shows, for a camera or by hand), `now_playing` (items 53, 53b:
  `enabled`, running (placed somewhere and Desk on); `placed`, a list of `wing_left`, `wing_right`,
  `strip` and `desk`; `source`, `adapter`, `fallback` or `off`; `state`, `playing`, `paused` or
  `none`; never a title, an artist or an app),
  `widget_visible`, `widget_visibility` (When the widget shows: `always`, `whileDeskOff` or
  `onRequest`; `always` when unset), `settings_open`, `settings_section`, `settings_anchor` (item 72: the
  section the page was opened at, such as `glow` on `notch`; null for the page's top), `settings_anchor_visible`
  (item 72, slice 3: that section's top line is inside the page's scrolling area; null with no anchor),
  `settings_preview` (item 68: the
  preview card the open section shows, `notch`, `layout`, `look`, `meters`, `message`, `nowPlaying`, `widget`,
  `menuBar`, `integrations` or `mods`; null for none or with Settings closed), `settings_preview_folded` (slice 3:
  the card is folded to its 44 pt strip, as on a window under 600 pt; null for a page without one), `mods_page` (item 64: `open`,
  the Mods page shows; `loaded`, it has read the folders; `folders`, `mods`, `plugins`, `enabled` (on, or a
  session's dev mod) and `missing` counts across folders; `checked`, how many Checks have answered; `cli`,
  `claude` was found; never a mod's name, a path or a report), `message_editor` (item 69: `open`, Settings,
  Message shows; `mode`, `list` or `text`; `rows`, `styled` and `raw` message lines and `notes`, comments
  and blank lines, in the editor's document; `unsaved`; `today_special`, how many date lines the Desk draws above the
  usual line today; `added`, the line `message-editor add` put in, else
  null; never one of your lines), `meters` (tier, label, percent, fill,
  pace, reset, `warning`: the row draws red with the ink glow, per Settings, Desk, Meters), `widget_warnings` (the
  widget's tiers drawing red with a glow, same rule and settings, in display order), `meetings_count`, `desk_frames` (every interactive Desk element while Desk runs: `kind` is `meters`, `meter_row`, `account`, `note`, `meeting_row`, `meetings`, `now_playing`, `now_playing_next`, `watcher`, `av_indicators`, or, while "Clock and message take clicks" is on, `clock`, `message` and `claude_line` (the account name nests inside the claude line); `key` is the limit's tier for a meter row, the row's index for a meeting row and `desk` or `strip` for now playing, never a title or a label; `clickable`; `frame` as `[x, y, w, h]` in whole points from the Desk window's top left), `desk_frames_ok` (the app's own check of those frames: each drawn element has a non-empty frame inside the Desk window, rows lie within their block, and no two click areas of different kinds overlap, slack included) and `desk_frames_problem` (why not, such as `meters frame empty`; null when ok), `desk_piece_clicks` (Settings, Desk, Look's "Clock and message take clicks", desk suite key `piecesTakeClicks`; `scenarios/desk-layout.yaml` flips it and checks the clock's frame comes and goes), `dock` (item 56: `side` `bottom`, `left` or `right` and `autohide`, the Dock's own settings, read only; `inset`, the points the Desk's corners on that side are moved in now: the Dock's reach when always shown, its depth while an auto-hiding Dock shows, 0 otherwise), `alerts`, `last_fetch`, `active_tool`, `pacing_pinned`,
  `pulse_count`, `glow_count` (notch glows fired so far, drawn or not; a pulse fires one too), `glow_shape` (what the last
  drawn glow outlined: `island`, `plain` for the hardware notch alone, `top` for Claude Code's glow at the top center of a screen without a notch, or `none` yet), `glow_alerts`, `glow_meetings`,
  `glow_camera` (the three Glow switches in Settings, Desk, Notch), `glow_claude_waiting`, `glow_claude_done` (the two Glow for Claude Code switches, item 51), `theme` (the widget theme's id), `menu` (groups with item titles and checkmarks), `menu_submenus` (the submenus every Sanduhr menu shows after Show or Hide Widget, by title: `Accounts` with two or more accounts, then `Menu Bar Shows`), `credentials_store` (`keychain` or `file`: where the session key lives this launch, never the value), `account_ref` (the active account as snapshot.json names it: 8 hex digits of a hash of its label, never the label; null with no accounts), `accounts_count`, `history_days` (the active account's Meter history: 30, or 0 when off; item 43), `data` (the active account's data choices, item 44: `activity` `off`/`live`/`record`, `names` `names`/`hidden`/`full`, `share` `off`/`meters`/`activity`, and `folder_linked` true or false; never the folder's path or the label), `local_activity` (live Claude Code activity, item 45: `reading` true while the shown account's linked folder is read for the cards, which needs activity Live only or Keep a record and a linked folder, and `events`, the usage events counted since the last refresh; never a path, project or model), `vault` (the active account's record, item 46: `recording` true while its activity is Keep a record with a folder linked, `months` the session-shard months kept, `last_ingest_ok` whether the last cycle for its folder completed this launch; never a path, project, folder id or label), `integrations` (items 49 to 51: `mcp_installed`, `statusline_installed`, `meters_installed` and `hooks_installed`, how many Claude Code folders hold Sanduhr's MCP server, statusline, meters mod and notch glow hook entries, current or outdated; counts only, never a path), `pending_suggestions` (items 54, 55: `messages` and `theme`, whether a suggestion from Claude waits for the user; flags only, never its content), `follow` (Follow the account I'm using), `follow_paused` (a manual switch is holding following back), `version`, `build`, `whats_new` (item 57: `last_seen`, the version whose cards were last shown or recorded, null before any; `pending`, how many cards the next launch would show, 0 with Don't show after updates on; `open`, the window shows; `hide_after_updates`, that switch), `tour` (item 61: `open`, the window shows;
`step`, its step from 1, 0 when closed; `steps_shown`, how many steps this Mac shows; `done`, finished or
skipped; `pending`, a fresh install's tour waiting for its first successful fetch). The Accounts submenu is not in `menu`: its items are labels.

Actions: `show-widget`, `hide-widget`, `settings-link "<Page> Settings…"` (Settings at the page a button with that title opens, as the button opens it; three dots work for the ellipsis; a title that names no sidebar page is an error), `settings [section [anchor]]` (a `SettingsSection` raw value such
as `notch` or `deskLayout`, or a retired one such as `deskMeters`; with an anchor, `notch glow` or `notch#glow`,
the page scrolled to that section, opening its Advanced disclosure when it is under one; an anchor the page lacks is
an error naming the ones it has), `settings-search <words>` (the sidebar search with the words typed and Return
pressed: the best match opened, scrolled to and lit; words that find nothing are an error), `close-settings`, `refresh` (waits for the fetch), `test-alert`,
`pulse [tier]` (`five_hour` by default; it glows the notch too), `tool deep-work|pacing|snake` (as the Tools menu: chosen
again it closes), `desk on|off`, `notch on|off`, `camera-light on|off` (the light by hand, as Tools, Camera Fill Light;
its window is kind `camera` in tree.yaml, one node labeled `Camera fill light`), `glow [alert|meeting|camera|claude-waiting|claude-done]`
(the notch glow once, whatever its switches; around the island while Desk runs with it on, else around
the plain hardware notch on a notched screen, in a click-through window of kind `glow` labeled `Notch glow` that fades out after about three seconds),
`theme <id>` (the widget theme by id, as the Theme menu and the gallery pick it, such as `obsidian`
or `match-desk`; an unknown id answers with an error listing the ids), `account next` (switches to the next
account, as a click on the widget's chip; a manual switch, so it pauses following. No action adds, renames,
signs out or removes an account, and no scenario switches one: a smoke run works on your real accounts),
`whats-new` and `close-whats-new` (the What's New window, item 57, with every card up to this version, as About
opens it; window kind `whats-new`. Nothing is recorded as seen), `tour`, `tour-step <n>` and `close-tour` (the
welcome tour, item 61, at step 1 or step n, as Take the Tour… opens it; window kind `welcome-tour`. Nothing is
recorded: the tour's state and What's New's last-seen version stay as they were),
`message-editor add|revert` (item 69: Settings, Message's editor; add opens it and adds the smoke's own line,
Fridays, "ship it." in a sunset gradient in script with a sweep, as the controls write it; revert drops
unsaved edits. Neither saves, so `messages.txt` is never written; a run reverts what it added),
`desk-arrange start|test|done|cancel` (item 60: Arrange mode on the Desk, as Arrange Desk… enters it; test
makes the smoke's own edit, the clock to Top right at 120%, in the working layout only; done ends it writing
the layout once when it changed, cancel ends it writing nothing. A run that leaves it on cancels it).

## Scenarios

```yaml
name: Settings opens at Notch
needs: [desk]                     # desk, notch, credentials, widget, meters; unmet skips with a reason
steps:
  - do: settings
    arg: notch
  - expect:
      window: settings
      contains: { label: Extend the camera notch }   # every given field must match
  - expect:
      window: widget
      missing: { text: "/^Session/" }               # "/regex/" (flags i, m, x) on any field
  - expect_state: { settings_section: notch, meters.0.label: Session (5hr) }
  - defaults: { domain: desk, key: notchWings, type: float, value: 50 }  # desk or widget alias
  - wait: 1
  - snap: notch-settings
```

`expect_state` keys are dotted paths: `meters.0.label` indexes a list, and a part such as
`desk_frames[kind=meter_row]` picks the first list item whose field reads as that text
(`desk_frames[kind=meter_row].frame.3` is that row's height). The `meters` need skips a scenario
when the Desk layout has no meters.

`expect` and `expect_state` retry for up to 3 s (`within: <seconds>` on `expect` changes it),
since the UI settles after an action. A failing step stops the scenario. Either way, at the end
every default the scenario wrote is put back (deleted if it was not set), and Desk, notch, widget,
the tools, the widget theme and the Settings window are put back as they were. `run` prints a line per step and a
summary, writes `out/run-<time>/report.yaml` with the snaps beside it, and exits 1 on a failure.

The shipped scenarios cover the automatable parts of `docs/mac-smoke-test.md`. Fresh-install and
migration checks stay manual: they wipe real defaults. A `settings` or `show-widget` action
brings Sanduhr forward, as clicking would.

## Several copies on one Mac

A dev build, an installed copy and DMG stages all share the bundle id `com.626labs.sanduhr`.

- The CLI talks to the running copy by path and never starts one: if the copy it targets has
  quit, the call fails (a `watch` keeps going and says so) instead of launching it.
- Quit a copy by process id (`kill <pid>`), not `osascript -e 'quit app id "com.626labs.sanduhr"'`:
  with several copies on disk, that can launch whichever one macOS prefers and quit that one.
- `watch` stops on Ctrl-C, TERM or HUP, also when started in the background.

## Privacy

Everything stays local in `mac/smoke/out/` (gitignored). Captures are of Sanduhr's own windows
only, never the full screen, but the Desk renders and tree still show meeting titles and the
message line, and state.yaml has your usage numbers. Don't paste them anywhere public.

## Self-test

`ruby mac/smoke/selftest.rb` runs the matcher, dotted state lookups, defaults restore, the restore
plan, the gallery and whole scenario runs against `fixtures/` with a fake app. No running app,
no real defaults.
