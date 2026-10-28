## Sanduhr für Claude 4.0 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v4.0.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8 or later, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. A 3.7.x Setup.exe install needs this Setup.exe once. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Settings, rebuilt.** Pages are grouped by what you came to do, and the search box finds any setting and opens the page right at it. Every feature has one home.
- **Settings ▸ Privacy** lists every place Sanduhr connects to, when, what it carries and the switch that turns it off, and every switch that lets Claude change your Desk, themes or widgets.
- **One theme for the widget and the Desk.** Desk Look ▸ Follow the widget theme gives the Desk the widget's colors, font and plate. **Use the theme's colors for the message** makes the message match too, and keeps your own colors for when you switch back.
- **Streamer mode** (Settings ▸ Privacy, off by default): account, folder and project names become codenames on the widget, the Desk, the tray and in Settings, while the live numbers stay.
- **Continue with Google works** in the sign-in window: Google's sign-in opens in a small window that closes itself when it's done.
- **Mods & Widgets** puts everything installed for Sanduhr and for Claude Code in one list.
- **Clear one account's history** from Settings ▸ Accounts, and **Reset Sanduhr…** can leave a Claude Code folder's entries alone: untick it.
- **Updates on your terms** (Setup.exe): Settings ▸ About ▸ Updates ▸ Check for updates once a day, on by default.

Nothing new leaves your PC: 4.0 adds no destination. The Privacy page now names three it already used: Google's sign-in page, claude.ai's routines budget and WebView2's SmartScreen.

The full list is in the changelog.

### Known issues

- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v400--2026-10-28)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
