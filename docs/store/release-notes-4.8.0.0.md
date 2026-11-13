## Sanduhr für Claude 4.8 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v4.8.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8 or later, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Codex limits, beside Claude's.** Tick Codex in Set up Sanduhr or Settings ▸ Usage ▸ Sources and the widget shows Codex's 5-hour and weekly limits under Claude's, with the same pace marker and burn projection. Sanduhr reads them from the logs Codex already writes on your PC, so there's no sign-in and nothing is sent anywhere. Each limit says "as of" the last time Codex wrote. After 30 quiet minutes it dims and drops its pace and burn, so an old number never reads as current.
- **Both plans on one bar.** With Claude and Codex on, combined bars fill to your primary plan and carry on in the other plan's color when it's further along. Pick the primary, each plan's color and its alerts on the new Sources page. The tray follows your primary.
- **Codex on the Desk, in the MCP and in the statusline.** Desk meter pieces can show Combined, Claude or Codex. The MCP's `get_usage` and the Claude Code statusline (`cx 5h 40% wk 12%`) include Codex too.
- **Name your own codenames.** In Settings ▸ Privacy ▸ Show the real name, **Rename…** lets you pick the codename streamer mode shows for each account, folder or repo.

### Fixes

- **A widget pack zip you made yourself installs.** Install widget from file… reads the zips Explorer, Finder and zip tools make, including a folder name with spaces.
- **Installing the MCP server checks that it starts,** and says why when it doesn't.

Nothing new leaves your PC: 4.8 adds no destination and no capability. Codex's limits are read on this PC and kept in two local files, listed in the [privacy policy](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md).

### Known issues

- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, and "Codex" is a trademark of OpenAI, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC or OpenAI.
