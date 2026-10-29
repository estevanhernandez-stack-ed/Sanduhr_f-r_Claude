## Sanduhr für Claude 4.0.1 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v4.0.1/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8 or later, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### Fixed

- **The layout map shows every monitor.** Settings ▸ Desk ▸ Layout map now draws each connected monitor where Windows arranges it, with its own taskbar and the pieces the Desk draws there. A Desk on a second monitor no longer shows an empty map.

Nothing new leaves your PC: 4.0.1 adds no destination and no capability.

### Known issues

- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v401--2026-10-29)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
