# Mac smoke automation

Tools that let a person or an agent see and check the running Mac app without clicking through
`docs/mac-smoke-test.md` by hand: window screenshots, a YAML dump of the UI tree (the Mac
counterpart of inspecting XAML on Windows), live watching, and YAML scenarios that drive the app
and assert on what it shows. Development tooling only; nothing here is user-facing.

## Constraints that shape it

- **Privacy on a work Mac.** A full-screen capture records everything on screen. The tools
  capture **Sanduhr's own windows only** (`screencapture -l <window id>`), and write to
  `mac/smoke/out/`, which is gitignored. Desk windows still show meeting titles and the message
  line, so outputs stay local.
- **Permissions.** Reading another app's accessibility tree needs Accessibility permission for
  the caller, which terminals and agents don't have. The app can always read its own tree, so the
  tree dump runs **inside the app**. Window screenshots use `screencapture`, which works from a
  process with Screen Recording permission (VS Code's terminal has it); without it, the in-app
  renders still work.
- **Off by default.** The in-app hooks answer only when `defaults write com.626labs.sanduhr
  debugHooks -bool true` is set (or `SANDUHR_DEBUG_HOOKS=1` is in the environment). A shipped
  build ignores every `sanduhr://debug/...` link otherwise.

## In-app hooks (`mac/Sources/Sanduhr/Debug/`)

All through the existing URL handler, `sanduhr://debug/<command>?…`. Each command writes its
results into the directory it is given, then writes `done` (or `error` with a message) last, so
the caller can wait for it.

| Command | Does |
|---|---|
| `snapshot?dir=<path>` | For every Sanduhr window on screen (widget, Desk layer, notch wings, Settings, any sheet): `<name>.png` rendered in process, `tree.yaml` (all windows), `state.yaml` |
| `action?name=<name>[&arg=<value>]` | Drives the app: `show-widget`, `hide-widget`, `settings` (arg = section), `close-settings`, `refresh`, `test-alert`, `pulse` (arg = tier), `tool` (arg = deep-work, pacing, snake), `desk` (arg = on/off), `notch` (arg = on/off) |

`tree.yaml`, one entry per window, children nested, each node:

```yaml
- window: settings          # widget | desk | notch | settings | sheet
  title: Sanduhr Settings
  frame: [x, y, w, h]       # screen points, top-left origin
  visible: true
  tree:
    - role: AXToggle
      label: Show Desk
      value: 1
      enabled: true
      frame: [x, y, w, h]
      children: [...]
```

**As built:** the in-process accessibility walk returns only each window's `AXHostingView`,
because SwiftUI builds its element tree only for a real assistive client (setting
`AXEnhancedUserInterface` on the app from inside does not count). So the tree also carries an
`OCRText` node for every line of text drawn in the window, recognized with Vision from the
in-process render. Scenarios match on those, and on `state.yaml` for switch values. Live run on
2026-10-02: 10 of 10 scenarios pass.

`state.yaml`: Desk enabled and running, layout, notch, widget visible, Settings open and its
section, meter rows (label, percent, fill, pace, reset), meetings count, alert settings, last
fetch time, app version and build.

## CLI (`mac/smoke/smoke`, Ruby, no gems)

| Command | Does |
|---|---|
| `smoke snap [label]` | Snapshot into `out/<time>-<label>/`: in-app renders, `tree.yaml`, `state.yaml`, plus on-screen captures of each Sanduhr window (`screen-<window>.png`); prints the folder |
| `smoke tree [window]` | Snapshot and print the tree (optionally one window) |
| `smoke state` | Snapshot and print `state.yaml` |
| `smoke do <action> [arg]` | Run one hook action and wait for it |
| `smoke watch [seconds]` | Stream Sanduhr's log live and refresh `out/watch/` every N seconds (default 5); `out/watch/index.html` reloads itself |
| `smoke run <scenario.yaml>…` | Run scenarios, print pass/fail per step, exit non-zero on any failure, leave a report folder |
| `smoke view [folder]` | Build and open an HTML gallery of a snapshot or run |
| `smoke enable` / `disable` | Turn the in-app hooks on or off |

Scenario format (`mac/smoke/scenarios/*.yaml`):

```yaml
name: Settings opens from the notch shortcut
needs: [desk]                      # skip with a reason if unmet (desk, notch, credentials)
steps:
  - do: settings
    arg: notch
  - expect:
      window: settings
      contains: { role: AXToggle, label: Extend the camera notch }
  - expect_state: { settings_section: notch }
  - snap: notch-settings
  - do: close-settings
```

Step kinds: `do` (hook action), `defaults` (write a key: domain, key, type, value), `wait`
(seconds), `snap` (label), `expect` (window plus `contains` / `missing` node match, all given
fields must match, `label` may be a regex in slashes), `expect_state` (dotted keys in
`state.yaml`). A scenario that changes defaults restores them at the end, pass or fail.

## Scenarios shipped with it

Automatable parts of `docs/mac-smoke-test.md`: Settings sections and Surfaces switches, Desk on
and off, notch on and off and back (the wings bug), meters present and matching the widget,
widget show and hide, Tools menu actions, test alert with Desk delivery (pulse), the menu model.
Fresh-install and migration checks stay manual: they wipe real defaults.
