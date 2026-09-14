## Sanduhr für Claude 3.4.2 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.4.2/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. Store installs update through the Microsoft Store. The `.msix` on this page is the unsigned Store upload artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What changed

- **Sanduhr tells you when a Store update is waiting.** Windows cannot replace files a running program holds, so a Store update attempted while the widget is running fails and quietly leaves you on the old version. Sanduhr now asks the Store every few hours, and when an update is waiting it says so in the status line and raises a notification whose button opens the Store's downloads page and closes the app so the install can finish. Store installs only; the Setup.exe channel already updates itself.
- **Usage counts a subfolder as its repository.** A Claude Code session started in a subfolder of a repository used to appear as its own project in the Claude Usage tab, the MCP burn tool, and the published record. It now counts toward the repository, the way sessions in a worktree already did. Nested repositories keep their own names.
- **The theme editor scrolls.** In Settings ▸ Themes ▸ Studio the token list could push "Save & apply" below the bottom of the window, and the only way to reach it was to drag the window taller. The editor scrolls now; installed themes and their buttons stay put.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v342--2026-09-14)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
