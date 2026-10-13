## Sanduhr für Claude 3.5.0 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.5.0/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. Store installs update through the Microsoft Store. The `.msix` on this page is the unsigned Store upload artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **The Desk.** Sanduhr can now draw on your desktop, behind your windows: a clock and date, your usage meters, your own messages, what's playing, CPU, memory and disk, and the weather for a city you choose. Drag the pieces where you want them with Arrange Desk…, and pick light, dark or automatic ink to suit your wallpaper. It is off by default; turn it on in Settings ▸ Desk.
- **Just the Desk.** No Claude account? Choose Just the Desk on the first-run card and use the Desk on its own. The widget stays hidden and the tray icon brings it back; sign in any time to get the Claude meters.
- **What's New and a welcome tour.** After an update you see what changed, with a Show me button for each item. A new install gets a short tour. Both open again from the tray menu or Settings ▸ About.
- **Open Settings from any app** with a shortcut you set (Ctrl+Alt+S by default, off until you turn it on).
- **Pick what the tray shows:** the highest limit, the session, the week, whichever is higher, or a rotation.
- **Limits that come and go.** Hide a temporary limit until it resets, stop its warnings, and bring hidden limits back from the Hidden limits menu.
- **Settings, reorganized,** with a grouped sidebar and a search box that jumps straight to the control you name.
- **A Claude Code page.** Every Claude Code folder on your PC is found and counted. Link a folder to an account, choose which folders the MCP server may read, and install or remove the statusline, the MCP server, the meters mod and the glow from one place. Every change keeps a backup and can be undone exactly. Mods & Config shows what each folder has set up, read-only.
- **The glow.** When Claude Code needs you or finishes, the top of the window you are working in lights up in your theme's colors. Off by default.
- **Follow the account I'm using** switches the widget to the account you are actually burning through. Off by default.
- **Reset Sanduhr…** in Settings ▸ About removes what you pick (Claude Code entries, accounts, history and the vault, themes, settings and the Desk messages), then quits.
- **Demo mode** (`--demo`) shows sample accounts for screenshots and never touches your real data.

The full list is in the changelog.

### Going back to an earlier version

Going back to a version before 3.5 turns the Settings shortcut off; your keys are kept and come back when you update again.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again. A WebView2 process left behind by the previous version can keep running after the widget quits, and Windows waits for it to close.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v350--2026-10-12)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
