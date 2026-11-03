## Sanduhr für Claude 4.6 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v4.6.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8 or later, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **The Desk sits under your desktop icons.** Your icons and fences draw over the clock, meters and the rest, with the same soft edges as before. It's on for everyone; **Settings, Desk Look, Keep the Desk under desktop icons** turns it off.
- **Right-click a bare part of a widget for the Desk menu.** On an icon or empty desktop you still get Explorer's menu. Clicking now playing still plays or pauses, and dragging from a widget still starts a selection.
- **The Launcher and drawers stay above your icons**, so they glow, launch and drag as before.

Nothing new leaves your PC: 4.6 adds no destination and no capability. To keep widgets clickable under the icons, Sanduhr watches for mouse presses over the Desk and acts only on those; it never records a press and never sees the keyboard.

### Known issues

- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v460--2026-11-03)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
