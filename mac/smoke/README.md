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
smoke/smoke tree settings      # the Settings window's UI tree (widget, desk, notch, camera, settings, sheet)
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
- **state.yaml**: `desk_enabled`, `desk_running`, `layout`, `notch`, `has_notch`,
  `notch_left`, `notch_right`, `notch_strip` (what each place on the island shows, a
  `NotchContent` raw value such as `meetingOrTime`, `meters` or `nothing`; the default when unset),
  `camera_in_use` (an app is using a camera; only watched while the camera light switch is on),
  `camera_light` (the camera light shows, for a camera or by hand),
  `widget_visible`, `widget_visibility` (When the widget shows: `always`, `whileDeskOff` or
  `onRequest`; `always` when unset), `settings_open`, `settings_section`, `meters` (tier, label, percent, fill,
  pace, reset, `warning`: the row draws red with the ink glow, per Settings, Desk, Meters), `widget_warnings` (the
  widget's tiers drawing red with a glow, same rule and settings, in display order), `meetings_count`, `alerts`, `last_fetch`, `active_tool`, `pacing_pinned`,
  `pulse_count`, `glow_count` (notch glows fired so far, drawn or not; a pulse fires one too), `glow_shape` (what the last
  drawn glow outlined: `island`, `plain` for the hardware notch alone, or `none` yet), `glow_alerts`, `glow_meetings`,
  `glow_camera` (the three Glow switches in Settings, Desk, Notch), `theme` (the widget theme's id), `menu` (groups with item titles and checkmarks), `credentials_store` (`keychain` or `file`: where the session key lives this launch, never the value), `account_ref` (the active account as snapshot.json names it: 8 hex digits of a hash of its label, never the label; null with no accounts), `accounts_count`, `follow` (Follow the account I'm using), `follow_paused` (a manual switch is holding following back), `version`, `build`. The Accounts submenu is not in `menu`: its items are labels.

Actions: `show-widget`, `hide-widget`, `settings [section]` (a `SettingsSection` raw value such
as `notch` or `deskLayout`), `close-settings`, `refresh` (waits for the fetch), `test-alert`,
`pulse [tier]` (`five_hour` by default; it glows the notch too), `tool deep-work|pacing|snake` (as the Tools menu: chosen
again it closes), `desk on|off`, `notch on|off`, `camera-light on|off` (the light by hand, as Tools, Camera Light;
its window is kind `camera` in tree.yaml, one node labeled `Camera light`), `glow [alert|meeting|camera]`
(the notch glow once, whatever its switches; around the island while Desk runs with it on, else around
the plain hardware notch on a notched screen, in a click-through window of kind `glow` labeled `Notch glow` that fades out after about three seconds),
`theme <id>` (the widget theme by id, as the Theme menu and the gallery pick it, such as `obsidian`
or `match-desk`; an unknown id answers with an error listing the ids), `account next` (switches to the next
account, as a click on the widget's chip; a manual switch, so it pauses following. No action adds, renames,
signs out or removes an account, and no scenario switches one: a smoke run works on your real accounts).

## Scenarios

```yaml
name: Settings opens at Notch
needs: [desk]                     # desk, notch, credentials, widget; unmet skips with a reason
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
