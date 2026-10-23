## Sanduhr für Claude 3.7.2 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.7.2/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. **This is a GitHub (Setup.exe) update only.** It does not go to the Microsoft Store: Store installs stay on 3.7.1 and get these fixes in 3.8. The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's fixed

- **Updates no longer fail because of a leftover WebView2 process.** After you quit Sanduhr, a WebView2 process it started could keep running, and Windows refused to install the next update (error 0x80073D02) until you restarted. Sanduhr now closes its WebView2 processes before it exits, and Windows ends any it misses when Sanduhr exits, even after a crash or End task.
- **Only one Sanduhr runs at a time.** Opening Sanduhr again while it runs now brings up the widget you have instead of starting a second one with a second tray icon.
- **Screen readers can choose a theme.** In Settings ▸ Themes and the widget's theme switcher, selecting a theme with a screen reader, voice access or the arrow keys now applies it, not only a mouse click (#316).
- **Every folder's controls say which folder they belong to.** Settings ▸ Claude Code reads "Linked account for ~\.claude-personal" instead of the same "this folder" on every row, and each line in Settings ▸ Desk messages reads with its number.
- **Now playing shows the app's name.** Settings ▸ Now playing shows "YouTube Music" where it showed a web app's raw id ("Chrome._crx_…") (#317).
- **Launcher labels show in full.** A label wraps to two lines under its button, and the buttons widen when a long label needs it, so a label up to the 24-character limit draws whole instead of "YouTube…" (#318).
- **The design review bar opens where the design is.** When Claude proposes a Desk design for a second monitor, **Apply** and **Dismiss** now open on that monitor, not the main one. Arrange Desk's **Done** and **Cancel** follow your pieces the same way (#319).

The full list is in the changelog.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs (3.7.1): if an update won't install (error 0x80073D02), quit Sanduhr from the tray, wait a few seconds and try again, or restart Windows. 3.8 brings the fix above to the Store.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v372--2026-10-22)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
