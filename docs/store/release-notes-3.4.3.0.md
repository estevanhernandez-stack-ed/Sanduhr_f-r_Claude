## Sanduhr für Claude 3.4.3 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.4.3/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. Store installs update through the Microsoft Store. The `.msix` on this page is the unsigned Store upload artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What changed

- **On the Store version, Claude Code can see Sanduhr again.** The Store build saved its files in a private folder that only the app itself could see. Claude Code, the `sanduhr-mcp` server and the statusline run outside the app, so they found an empty or half-installed `%APPDATA%\Sanduhr`: the MCP server would not start, and the statusline had no numbers. The Store build now saves to the real `%APPDATA%\Sanduhr` and `%LOCALAPPDATA%\Sanduhr`, the same folders the Setup.exe build uses. If you had already installed the MCP server or the statusline, Sanduhr puts fresh copies in the real folder when it starts, so there is nothing to reinstall; open a new Claude Code session to pick them up.
- **Your settings, history and archive come with you.** The first time 3.4.3 starts, it copies what earlier Store versions saved in that private folder to the real folders, once. Nothing is deleted; the old copy stays inside the app until you uninstall it.
- **Uninstalling the Store version now leaves your data behind,** like any desktop app. To remove it, delete `%APPDATA%\Sanduhr` and `%LOCALAPPDATA%\Sanduhr` after uninstalling. [PRIVACY.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md) has the details.
- The Store version now needs Windows 10 version 1903 or later.

Setup.exe installs change nothing in this release.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update to 3.4.3 won't install (error 0x80073D02), restart Windows and try again. A WebView2 process left behind by the previous version can keep running after the widget quits, and Windows waits for it to close.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v343--2026-10-11)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
