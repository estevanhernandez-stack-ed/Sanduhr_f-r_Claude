## Sanduhr für Claude 4.9 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v4.9.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8 or later, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **Every agent's tokens, side by side.** Codex and the Antigravity CLI now count beside Claude Code. Sanduhr reads the logs they already write on your PC, so there's no sign-in and nothing is sent anywhere. Codex follows its switch in Settings ▸ Usage ▸ Sources; the Antigravity CLI has its own "Activity only" switch there, off by default.
- **See it live in the footer.** The widget footer shows each agent's new tokens since the last check: "CC +12k · Codex +3k · Agy +1k". Agents run about five minutes behind Claude Code.
- **One Local burn page for all of them.** The 30-day strip stacks each day by agent color, each project says which agents it came from ("13.0M (Codex)"), and the session ledger labels every session with its agent and can filter by one. The ledger CSV has an `agent` column at the end of each row.
- **Claude can read them too, if you say so.** With the MCP server installed, turn on "Let Claude read Codex's project totals" (or Antigravity's) under Settings ▸ Claude Code ▸ Folders. Each is off by default.

The Antigravity IDE's usage can't be read (it keeps it in a format Sanduhr doesn't decode), and Antigravity's limits aren't visible, so only the CLI's activity counts.

Nothing new leaves your PC: 4.9 adds no destination, no capability and no library. The counts stay in Sanduhr's local usage vault, listed in the [privacy policy](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md).

### Known issues

- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, "Codex" is a trademark of OpenAI, and "Antigravity" is a trademark of Google LLC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC, OpenAI or Google LLC.
