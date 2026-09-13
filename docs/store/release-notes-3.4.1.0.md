## Sanduhr für Claude 3.4.1 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.4.1/626Labs.Sanduhr-win-Setup.exe) (per-user install, updates itself). Existing Setup.exe installs update on their own within about a day. Store installs update through the Microsoft Store. The `.msix` on this page is the unsigned Store upload artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What changed

- **One-click MCP install.** Settings ▸ Claude Usage ▸ Install MCP server registers `sanduhr-mcp` with the one Claude Code home you pick, after a consent dialog that also asks which homes the burn tool may read (none by default). The server now ships inside the app as a self-contained `mcp\sanduhr-mcp.exe`, so it works on a machine with no .NET runtime, and the widget refreshes it on every start. Remove MCP server reverts everything.
- **Ask Claude Code for a theme.** With the server installed, "make me a Sanduhr theme from this album cover" becomes a sentence in the terminal: Claude Code calls `propose_theme`, the widget checks the palette against the design rules, saves it under `%APPDATA%\Sanduhr\themes\`, applies it, and tells the agent which theme was active before. A palette that fails the rules comes back with the fields to fix, and nothing is written.
- **Theme Studio.** Settings ▸ Themes ▸ Studio edits the fourteen color tokens and two glass dials with the widget itself as the live preview, findings as you type, Save & apply, Revert, and Copy JSON to hand the draft to an agent. Start from any built-in or installed theme.
- **Theme lint everywhere.** Pasted, dropped-in, and proposed themes all go through one validator: the schema as errors, the design rules as measured notes (dark base, text contrast on the card, a monochrome text ramp, a pace marker visible on every bar fill, one accent hue). A malformed color in a theme file is now skipped with a reason instead of crashing the apply.
- **Two more MCP tools, deeper pacing.** `get_model_usage` shows which model is burning the budget over 1, 7, or 30 days, joined with the live weekly meter; `get_usage_history` reads the durable usage vault for 7, 30, or 90 days of daily totals. `get_usage` now reports per tier how long to idle back onto pace, banked headroom, and where the meter lands at reset.
- **`sanduhr-mcp` names the widget floor.** With a widget older than 3.4.0 it answers `widget_too_old` with the remedy, and serves the widget's history file as a degraded reading instead of nothing.
- **Burn attribution rolls worktrees up.** Sessions under `.claude/worktrees` or `.worktrees` count toward the parent repo instead of appearing as their own projects.
- **Fixes.** Settings dropdowns no longer show the option's type name when closed; the Publish usage "Next:" line names the slot that actually fires next; the widget's tier list and the Settings lists use the themed scrollbar.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v341--2026-09-13)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
