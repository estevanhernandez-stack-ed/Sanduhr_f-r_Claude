## Sanduhr für Claude 3.8 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.8.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On a 3.7.x Setup.exe install, download this Setup.exe once and run it:** before 3.8 the app only noted a newer release and never installed it. From 3.8 on, updates download in the background and apply in the app when you click **Restart to update**. **This release goes to the Microsoft Store too:** Store installs go from 3.7.1 straight to 3.8, so they get 3.7.2's fixes as well. The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Widgets for your Desk.** Settings ▸ Widgets lists the 626 Labs widget catalog: the hourglass sprite and the OLED Neon theme, palettes and effects to start. Install, Update and Uninstall from the same page. Packs are data only (JSON, PNG and fonts, never code); the catalog is signed, and every pack is checked again on your PC before it installs.
- **The catalog is fetched only when you ask.** Opening Settings ▸ Widgets, or pressing **Check the catalog again**, asks GitHub for the catalog; a pack downloads only when you press Install or Update. Nothing polls in the background, and no account or identifier goes with the request.
- **A companion on the Desk.** A pixel hourglass that grows with the days you code (never with your usage), reacts to your pace, to a watcher waiting on you and to a reset, and naps when you're away. Show it, pick it and let it roam in Settings ▸ Widgets.
- **Claude can make you a widget.** Ask Claude Code for a pixel-art sprite, a palette or effects. It previews on your Desk with a bar that says what it is and lists its files; it installs only when you choose **Apply**, even with direct Desk changes on. **Use my own art…** puts your own PNG sprite sheet into a sprite made here; it stays on your PC.
- **Restart to update.** When an update is ready, the toast, the tray and the widget ask "Restart to update?" On yes, Sanduhr closes its browser helpers, installs and opens on the new version. Nothing happens until you click, and a failed or cancelled install leaves Sanduhr running with the offer still there.
- **Fold your Desk.** A Desk design from Claude Code can slant a stack of pieces up to 20 degrees either way, so two stacks read as the faces of a fold.
- **Call the weather anything.** Settings ▸ Weather ▸ **Show it as** puts a name like "Funky Town" on the weather piece instead of the city (#315). It never leaves your PC.
- **Livelier bars.** The bars beside now playing ease, fall and wear peak caps; **Gradient bars** (Settings ▸ Now playing) spreads your ink's colors across them. Still the playing app's level only: no audio is captured.
- **Meter styles:** the Claude line, the Claude limits and the system readings each take a style: bars, slim (half the height, one line), rings (small arc gauges side by side) or ticks (ten cells, the OLED-friendly one). Pace marks stay on every style. Pick them in Settings ▸ Desk or ask for them in a Desk design; a slanted stack stays inside its column.
- **Fixes.** The usage connection rebuilds itself when its browser exits, instead of stopping for good (#331). For Store installs, everything from 3.7.2: no leftover WebView2 process blocking the next update, one Sanduhr at a time, themes chosen by screen reader or keyboard apply, every folder's controls named for their folder, Now playing names web apps (#317), Launcher labels draw whole (#318), and the design review bar opens on the monitor the design is on (#319).

Claude Code needs the Sanduhr MCP server installed (Settings ▸ Claude Code) for widgets made with Claude and Desk designs.

The full list is in the changelog.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again. The 3.7.1 to 3.8 update still runs under 3.7.1, which can leave a WebView2 process behind; from 3.8, Restart to update closes it first.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v380--2026-10-xx)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
