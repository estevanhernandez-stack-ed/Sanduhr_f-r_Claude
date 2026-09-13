## Sanduhr für Claude 3.4.0 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.4.0.0/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. Store installs update through the Microsoft Store. The `.msix` on this page is the unsigned Store upload artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What changed

- **Your caps under the Claude Code prompt.** Settings ▸ Claude Usage ▸ Install statusline puts your usage percentages and reset times into the Claude Code statusline of the one home you pick, with your consent and a backup of that `settings.json` first. Remove statusline puts everything back. The widget writes a small local snapshot after each fetch (`%APPDATA%\Sanduhr\snapshot.json`: percentages, reset times, plan, and which widget version wrote it; never keys or account names), so a stale file from a development build reads as exactly that instead of a dead widget.
- **Publish usage, off by default.** Nothing about your usage comes back to us. If you want it somewhere, send it to yourself: once a day Sanduhr can post token counts per project name (folder names only, never paths) to an endpoint you choose. Settings ▸ Publish usage takes a preset (Custom endpoint, or the 626 Labs dashboard preset which fills the URL and auth for you), an endpoint URL, an auth scheme (bearer token, a header you name, or none), a masked token that lives in Windows Credential Manager, one share checkbox per Claude Code home (every home off until you tick it), a daily time (06:45 local by default), Publish now, and a status line. Nothing is sent unless the switch is on, an endpoint is set, and at least one home is ticked. Plain `http://` endpoints are accepted for loopback hosts only.
- **`sanduhr-mcp`.** A read-only MCP server (`get_usage`, `get_local_burn_by_project`, `ping`) that reads the same snapshot and your local Claude Code logs, built from `windows-dotnet/src/Sanduhr.Mcp`. It holds no credentials and makes no network calls. Its `publish_usage` tool queues the upload above through the widget and refuses with `disabled` / `no_endpoint` / `no_token` and the setting to fix, or answers `queued` when the widget is not running. `get_usage` and `ping` report the widget version that wrote the snapshot.
- Settings gains a `publish` group; a `publish_626` group from an earlier build migrates to the 626 Labs preset with your ticked homes intact, and a `626labs:agentKey` credential moves to `publish:token` on first use.
- `docs/PRIVACY.md` and `SECURITY.md` describe Publish usage as an optional, user-configured destination and list the exact payload.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v340--2026-09-13)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
