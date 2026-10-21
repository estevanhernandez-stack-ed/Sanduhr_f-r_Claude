# Desk Launcher — design (3.7.0, sub-project D)

Windows only. Part of 3.7.0, the Store release ("the agent-designed Desk"), with the DeskProposal
contract (A), the wallpaper summary (B) and per-song looks (C). This piece closes the last Rainmeter
gap that has no Sanduhr answer: Este's Knighthawk TouchButton, which opens the 626Labs Dashboard
Chrome app from the desktop. Decisions: dashboard `W7x5wo2nZ5d0lyfuZqDi` (3.7 scope) and this spec.

## Intent

Launch buttons on the Desk, so Sanduhr replaces a Rainmeter launcher skin. Safe enough that Claude
can later propose a Launcher (sub-project A) without being able to run anything you didn't install.

## What it is

- **One Launcher piece** (`launcher`), holding 1 to 8 buttons in a row (default) or a column. It is
  placed, sized, hidden and arranged like every other Desk piece (`DeskArrangement`, the eight
  anchors, Arrange hide/show, the plate per stack).
- **A button** is: a **target**, an **image**, an optional **label**, an optional **glow color**.
  - **Target** is exactly one of:
    - **app**: a Start-menu app's AppUserModelID, chosen from the `shell:AppsFolder` list (Win32
      programs, Store apps and Chrome/Edge PWAs alike; measured on Neb: "626Labs Dashboard" appears
      as `Chrome._crx_bhaoniopoe…`). Opened as `shell:AppsFolder\<AUMID>`.
    - **link**: an absolute `https://` URL (no other scheme), opened in the default browser.
  - **No command lines.** No file paths, no arguments, no `.lnk`/`.exe` targets. This is the safety
    property sub-project A relies on.
  - **Image**: the app's own icon by default (from the shell item); a link defaults to a link glyph.
    Or a PNG the user picks, copied into `%APPDATA%\Sanduhr\launcher\` (private storage; at most
    1 MB, decoded and re-encoded to a 256 px square on import). **No favicon download**: that would
    add an outbound destination to the Store disclosure.
  - **Label**: up to 24 characters, drawn under the image in the Desk font and ink. Off by default.
  - **Glow**: a soft halo on hover and on touch-down, in the Desk ink or a chosen hex color; a short
    press dip on click/tap.

## Behaviour

- The Launcher always takes clicks (like now playing), whatever "Clock and message take clicks"
  says. Each button is its own hit region; transparent pixels between buttons pass clicks to the
  desktop.
- **Mouse**: hover glows; left-click launches. **Touch**: touch-down glows; touch-up inside the
  button launches; a drag out cancels.
- A **missing target** (app uninstalled) draws dimmed; a click opens Settings ▸ Desk ▸ Launcher at
  that button instead of failing silently.
- The Desk sits below normal windows, so a button works where the Desk is uncovered (or after Show
  desktop), the same as Rainmeter. Stated, not a bug.
- Launching never elevates and never waits; a failed launch shows the standard Sanduhr toast with
  the app's name.

## Settings ▸ Desk ▸ Launcher

- The button list: add, remove, reorder (buttons and keyboard: Move up / Move down).
- **Add app…** opens a searchable list of Start-menu apps with their icons.
- **Add link…** takes an `https://` URL and a label.
- Per button: **Change image…**, **Use the app's icon**, **Label**, **Glow color**.
- Row or column. Every control has an accessible name and is reachable from Settings search
  ("launcher", "button", "shortcut", "app").

## Data

- `settings.json` key `desk_launcher`: `{ "orientation": "row" | "column", "buttons": [ { "kind":
  "app" | "link", "target": "<AUMID or https URL>", "image": null | "<file name in launcher\\>",
  "label": null | "<text>", "glow": null | "#rrggbb" } ] }`. Absent key: no buttons; the piece is
  not placed by default.
- Images live in `%APPDATA%\Sanduhr\launcher\` (package storage on the Store build), named by a
  hash; removed when their button is removed and by Reset ("Settings" part).

## Core (pure, tested)

- `LauncherTarget.Validate(kind, target)`: app = an AUMID shape (no path separators, no spaces
  around, no quotes, at most 256 chars, `!` or a dotted package form allowed); link = `Uri` absolute,
  scheme exactly `https`, host present, at most 2048 chars. Anything else is refused with a reason.
- `LauncherModel`: parse/write the settings key (bad entries dropped, at most 8 buttons, label and
  glow validated), reorder, add/remove.
- `LauncherLayout`: button frames for a row or column at a size scale, hit-test a point to a button.

## App

- `LauncherApps` (interop): enumerate `shell:AppsFolder` (name, AUMID, icon via
  `IShellItemImageFactory`), resolve whether an AUMID still exists, launch via `explorer.exe
  shell:AppsFolder\<AUMID>` (or `IApplicationActivationManager` for packaged apps).
- The Launcher piece in `DeskView`; its click/hover/touch wiring through `DeskClicks`/`DeskHitTest`
  (a new `DeskElementKind.LauncherButton` with the button index as key).
- The Settings section and the app picker dialog.

## Privacy and the Store

- One new PRIVACY.md row: the Launcher's button list (app IDs and https links you chose, labels) in
  `settings.json`, and the images you picked in `%APPDATA%\Sanduhr\launcher\`. Nothing is sent
  anywhere; a link opens in your browser only when you click it.
- No new capability. Opening apps through the shell needs none.

## Testing

- Core: target validation (good and hostile inputs: `file:`, `javascript:`, `http:`, paths,
  arguments, quotes), model round-trip and limits, layout and hit-testing for row and column.
- WPF (`Sanduhr.App.Tests`): the piece's automation names per button, hit regions.
- Live on Neb: a button for "626Labs Dashboard" opens the PWA; a link opens the browser; hover and
  touch glow; an uninstalled target dims and opens Settings.

## Out of scope

- Command lines, scripts, file/folder targets, favicon fetching, per-button hotkeys, more than one
  Launcher piece, Claude proposing buttons (that arrives with sub-project A, through this model).
