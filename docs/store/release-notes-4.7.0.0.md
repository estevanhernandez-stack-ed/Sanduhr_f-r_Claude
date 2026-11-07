## Sanduhr für Claude 4.7 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v4.7.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8 or later, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Install a widget pack from a zip.** Settings ▸ Mods & Widgets has **Install widget from file…** for packs you made in a pack maker such as SpriteForge, or by hand. The zip gets the same checks as a catalog download, and a pack that fails installs nothing and says why. A companion goes straight onto your Desk, and installing the same pack again updates it.

### Fixes

- **The Desk's glow spot shows its color under your icons.** A Claude Code glow came out white there; it now glows in your theme's accent while Claude waits for you and green when a turn is done.
- **Sanduhr stays light during a long Claude Code session.** It used to re-read every session log from the start every 30 seconds; it now reads only what was added. On a 650 MB session log, the app dropped from about 10% of one CPU core to about 2%.
- **The Desk under your icons costs less at rest.** It's copied when something on it changes rather than every second, so the whole app now uses under 1% of one core when nothing moves.
- **The companion follows Windows' animation effects without a restart.** Turn Animation effects on and it walks within a second; turn them off and it stands still.

Nothing new leaves your PC: 4.7 adds no destination and no capability. A zip you install is read once on this PC and never sent anywhere.

### Known issues

- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v470--2026-11-07)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
