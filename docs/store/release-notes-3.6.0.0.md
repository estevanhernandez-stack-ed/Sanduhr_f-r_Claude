## Sanduhr für Claude 3.6.0 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.6.0/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. Store installs update through the Microsoft Store. The `.msix` on this page is the unsigned Store upload artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Now playing steps aside.** It shows only while music or video is playing, fades 5 seconds after you pause, and the pieces around it slide to fill the space. The bars move with your music, not with Windows' dings and chimes.
- **Watchers on the Desk.** Let Claude Code put a live card on your desktop for work it is watching (a CI run, a deploy): a title, progress, a short note, "waiting on you" when it needs you, and a fade when it passes. Turn on **Let agents show watchers** in Settings ▸ Desk and place **Watchers**. Off by default.
- **Claude can suggest Desk messages.** Ask Claude Code for a line or two; the suggestion waits on Settings ▸ Message until you **Add**, **Review** or **Dismiss** it. Nothing changes without Add.
- **A plate behind the Desk's text**, for a desktop with files under it. On for new installs; turn it on in Settings ▸ Desk.
- **Arrange Desk hides and shows pieces.** Hide a piece while arranging and bring it back from the Hidden strip; Done saves once, Cancel drops it all.
- **The statusline says which folder it's in.** In a Claude Code folder like `.claude-personal`, Sanduhr's line starts with `personal |`. Set `SANDUHR_LABEL` in that folder's settings to choose your own. Sanduhr stores no name; a plain `.claude` folder shows no label.
- **The tour knows the Desk**, and Just the Desk gets its own short tour.
- **Quit from any Settings page**, at the foot of the sidebar.

You need the Sanduhr MCP server installed in a Claude Code folder (Settings ▸ Claude Code) for watchers and suggested messages.

The full list is in the changelog.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again. A WebView2 process left behind by the previous version can keep running after the widget quits, and Windows waits for it to close.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v360--unreleased)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
