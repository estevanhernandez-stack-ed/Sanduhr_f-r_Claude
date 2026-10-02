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
smoke/smoke tree settings      # the Settings window's UI tree (widget, desk, notch, settings, sheet)
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
- **tree.yaml** is the accessibility tree, read by the app itself (no Accessibility permission
  needed). Each node: `role`, `subrole`, `label` (label or title), `value` (switches read 1 or 0),
  `enabled`, `frame` (screen points, top-left origin), `children`. SwiftUI puts a text's string in
  the label or the value, so scenarios match text with the `text` field, which checks both.
- **state.yaml**: `desk_enabled`, `desk_running`, `layout`, `notch`, `has_notch`,
  `widget_visible`, `settings_open`, `settings_section`, `meters` (tier, label, percent, fill,
  pace, reset), `meetings_count`, `alerts`, `last_fetch`, `active_tool`, `pacing_pinned`,
  `pulse_count`, `menu` (groups with item titles and checkmarks), `version`, `build`.

Actions: `show-widget`, `hide-widget`, `settings [section]` (a `SettingsSection` raw value such
as `notch` or `deskLayout`), `close-settings`, `refresh` (waits for the fetch), `test-alert`,
`pulse [tier]` (`five_hour` by default), `tool deep-work|pacing|snake` (as the Tools menu: chosen
again it closes), `desk on|off`, `notch on|off`.

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
the tools and the Settings window are put back as they were. `run` prints a line per step and a
summary, writes `out/run-<time>/report.yaml` with the snaps beside it, and exits 1 on a failure.

The shipped scenarios cover the automatable parts of `docs/mac-smoke-test.md`. Fresh-install and
migration checks stay manual: they wipe real defaults. A `settings` or `show-widget` action
brings Sanduhr forward, as clicking would.

## Privacy

Everything stays local in `mac/smoke/out/` (gitignored). Captures are of Sanduhr's own windows
only, never the full screen, but the Desk renders and tree still show meeting titles and the
message line, and state.yaml has your usage numbers. Don't paste them anywhere public.

## Self-test

`ruby mac/smoke/selftest.rb` runs the matcher, dotted state lookups, defaults restore, the restore
plan, the gallery and whole scenario runs against `fixtures/` with a fake app. No running app,
no real defaults.
