## Sanduhr für Claude 3.7.1 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.7.1/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). To update a Setup.exe install, download the new Setup.exe and run it; from 3.8 on, updates download and apply in the app. **This release goes to the Microsoft Store too:** Store installs update through the Store once it clears certification, from 3.5.0 straight to 3.7.1, so they get 3.6.0's changes as well. The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Claude can design your Desk.** Ask Claude Code to design your Desk to match your wallpaper. It reads how your Desk looks and a summary of your wallpaper, then proposes a whole look: where the pieces sit, the font, the ink, the plate, a Launcher, messages, song looks and a widget theme. It draws live on your real desktop with a bar that says what changed; you choose **Apply** or **Dismiss**. If the look needs a font you don't have, you get its download link and Apply waits.
- **Claude never changes the Desk behind your back.** Every proposal waits for you unless you turn on **Let Claude change the Desk directly** (Settings ▸ Desk, off by default). The picture of your wallpaper is a smaller copy that Claude can open only if you allow Claude Code to read that file.
- **Designs you can go back to.** Sanduhr keeps the last ten looks, with pinning, in Settings ▸ Desk ▸ Designs. **Put my Desk back**, in Settings and the tray menu, returns to the look before the last change.
- **A Launcher on the Desk.** Up to eight buttons that open Start-menu apps (including Chrome and Edge web apps and desktop programs) or `https://` links. Use the app's icon or a PNG of your own; a button glows when you hover or touch it. No command lines, ever: a button opens only what your Start menu lists.
- **A piece on each monitor.** Put each Desk piece on all monitors, the main one or a monitor you pick, from Settings ▸ Desk ▸ Where each piece sits or by dragging in Arrange Desk.
- **A look per song.** Turn on **Style what's playing** (Settings ▸ Now playing ▸ Looks, off by default) and each song is drawn in its own gradient and letter style, with the bars to match. Claude can suggest looks for your songs; they wait for your **Save**. The Claude Code now-playing mod shares the same looks.
- **OLED care.** The Desk and widget drift a few pixels every 3 minutes, dim after 5 minutes away and never hold pure white. Settings ▸ Desk ▸ OLED care, off by default.
- **Fixes.** Automatic ink and the wallpaper read use the picture Windows still draws when the original file is gone. A theme change no longer crashes Settings when the calendar was never opened. Screen readers hear every Settings list by name, not by type. Claude Code offers Update, not Install, for a piece already installed. Mods & Config opens on the folder Claude Code runs from and calls plugins plugins.

Claude Code needs the Sanduhr MCP server installed (Settings ▸ Claude Code) for designs and song looks.

The full list is in the changelog.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again. A WebView2 process left behind by the previous version can keep running after the widget quits, and Windows waits for it to close.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v371--2026-10-21)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
