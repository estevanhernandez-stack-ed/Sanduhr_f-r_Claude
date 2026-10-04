# Privacy Policy — Sanduhr für Claude

**Last updated:** 2026-10-03
**Publisher:** 626Labs LLC
**Contact:** [GitHub Issues](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues)

## Short version

Sanduhr für Claude is a **local desktop widget**. It does not run a server, does not have a backend, does not collect analytics, does not call home, and does not send your data to 626Labs or any third party.

The only network destination it contacts is `claude.ai` — and only with the credentials you yourself paste into it — to read your own Claude subscription usage so it can show it to you.

One opt-in exception, off by default: **Publish usage** lets you send a daily per-project token-count summary to an endpoint *you* choose, and only for the Claude Code homes you tick. Nothing about your usage comes back to us; if you want it somewhere, you send it to yourself. The 626 Labs dashboard is offered as a preset destination, one among any. Details in the table and the network section below.

## What data Sanduhr touches

| Data | Where it lives | Who can see it | What Sanduhr does with it |
| --- | --- | --- | --- |
| `sessionKey` (claude.ai cookie you paste in) | Windows Credential Manager, service `com.626labs.sanduhr`, slot `sessionKey:{Account}` per registered account | Only applications running as your Windows user account | Sent only to `claude.ai` when fetching that account's usage. Multi-account installs (v2.2.0+) store one slot per named account. |
| `cf_clearance` (optional Cloudflare cookie you paste in) | Windows Credential Manager, same service, slot `cf_clearance:{Account}` per account | Same as above | Sent only to `claude.ai` when fetching that account's usage |
| Account registry (the list of named accounts and which one is active) | Windows Credential Manager, slots `accounts:list` (JSON array of labels) and `accounts:active` (label string) | Same as above | Used by Sanduhr to route fetches and history writes to the right account. The labels are the names you choose ("Personal", "Work", etc.) — Sanduhr does nothing else with them. |
| Your Claude usage percentages (the numbers the widget displays) | `%APPDATA%\Sanduhr\history.{Account}.json` per account on your machine | Only your Windows user account | Stored for up to 30 days (rolling) so the widget can draw a sparkline and the History tab can chart trends. When the Claude Code snapshot below is missing or stale, `sanduhr-mcp` reads the latest point per tier from this file instead (percentages and reset times only; the account label in the file name is hashed the same way as in the snapshot, never returned). Never transmitted anywhere. |
| Your local Claude Code usage history (the "vault": one summary row per session (including its subagent transcripts) — project name, day-bucketed token totals per model, skill totals; full folder paths only if the hidden `vault_store_full_paths` setting — off by default — is enabled; never conversation content, never prompts) | `%LOCALAPPDATA%\Sanduhr\vault\` on your machine, one folder per Claude Code home you opt in | Only your Windows user account | Kept **indefinitely — unlike Claude Code's own logs, which Claude Code deletes after ~30 days**. Per-home opt-in at first run; erase any time via Settings ▸ Claude Usage (Erase archive / per-home purge) or by deleting the folder while Sanduhr is not running. Quarantined `.bad` recovery files in the same folder are part of the archive. Never transmitted anywhere by Sanduhr. When the MCP integration is installed, `sanduhr-mcp`'s `get_usage_history` tool reads the vault's daily totals and top project display names for the homes you ticked, and that answer becomes part of your Claude conversation like any other tool result. |
| Vault bookkeeping (`checkpoints.json`) | Same vault folder | Same | Hashed log-file identifiers only — no readable paths. Rebuilt automatically if deleted. |
| Usage snapshot for the Claude Code statusline (opt-in; exists only while the integration is installed) | `%APPDATA%\Sanduhr\snapshot.json` | Only your Windows user account — read by the statusline script Claude Code runs as you | Percentages, reset times, plan name, and a short account *hash* — **never the account label itself, never keys**. Rewritten each fetch, deleted on account switch, sign-out, or Remove. One caveat: what the statusline renders appears in your terminal like any other on-screen text. Never transmitted anywhere. |
| Statusline registration (opt-in, chosen home only) | The statusline script at `%APPDATA%\Sanduhr\bin\`, plus a `statusLine` entry in the **one** Claude Code home's `settings.json` you pick at install (a timestamped backup is saved beside it first) | Your Windows user account | Lets Claude Code render your caps under its prompt. "Remove statusline" in Settings ▸ Claude Usage reverts the entry (only if it still points at Sanduhr's script), deletes the script, and deletes the snapshot. |
| MCP registration (opt-in, chosen home only) | The `sanduhr-mcp` server copy under `%APPDATA%\Sanduhr\mcp\`, the launcher `%APPDATA%\Sanduhr\bin\sanduhr-mcp.cmd`, a `sanduhr` entry under `mcpServers` in the **one** Claude Code home's `.claude.json` you pick at install (a timestamped backup is saved beside it first), and the `mcp_roots` map in `settings.json` that records which homes the burn tool may read (every home off until you tick it) | Your Windows user account; the server runs as you when Claude Code starts it | Lets Claude Code ask for your live usage headroom and, only for the homes you ticked, per-project token totals (folder names, never paths). Tool answers become part of that Claude conversation, which is why each home has its own switch. The server can also hand the widget a color theme (`propose_theme`): the widget saves it as a file under `%APPDATA%\Sanduhr\themes\` and applies it; nothing is read, and the previous theme is one click away. "Remove MCP server" in Settings ▸ Claude Usage reverts the entry (only if it still points at Sanduhr's launcher) and deletes the server copy and launcher. |
| **Publish usage** (opt-in, off by default; no endpoint until you set one; every Claude Code home is unshared until you tick it) | POSTed to the endpoint URL you enter in Settings ▸ Publish usage (or the 626 Labs dashboard preset, `us-central1-project-626labs.cloudfunctions.net`, if you pick it) once a day at the time you set, or when you click "Publish now" / an agent calls the `publish_usage` MCP tool | Whoever operates the endpoint you chose. With the 626 Labs preset, that is your own dashboard account (the key you paste identifies it); 626 Labs receives nothing from any other destination | One record per day and machine: the date, your machine name, which homes you ticked, one token count per project **name** (folder basename only, never the path) from those homes' local Claude Code logs, a per-tier split, and the widget's current quota percentages (or an explicit stale/no-data marker). Never session content, prompts, account labels, or tokens. Body format is the plain snapshot object, or the same fields wrapped the way the 626 Labs preset expects. Re-publishing a day sends that day's record again. Turn it off in Settings ▸ Publish usage; untick a home to stop sharing it. |
| Publish destination (`settings.json` `publish` group: endpoint URL, auth scheme, header name, preset, publish time, which homes you ticked) | `%APPDATA%\Sanduhr\settings.json` | Your Windows user account | Read to know where and how to publish. Records only *whether* a token is stored, never the token. |
| Publish token (the credential you paste for your endpoint) | Windows Credential Manager, service `com.626labs.sanduhr`, slot `publish:token` (a pre-3.5 `626labs:agentKey` entry is moved here on first use) | Only applications running as your Windows user account | Sent only to the endpoint you configured, as `Authorization: Bearer`, as a header you name, or not at all if you set auth to None. Never written to a file. "Clear token" in Settings ▸ Publish usage deletes it. |
| Publish handoff files (only while the `publish_usage` MCP tool is in use) | `%APPDATA%\Sanduhr\publish-request.json` / `publish-result.json` | Your Windows user account | A request id + date, and the upload's status/message. No usage data, no token. Cleared by the widget after each request; a request expires after 24 hours. |
| Your theme preference and last window position | `%APPDATA%\Sanduhr\settings.json` | Your Windows user account | Read at startup to restore your setup |
| Operational logs | `%APPDATA%\Sanduhr\sanduhr.log` (rotating, 1 MB × 3 files) | Your Windows user account | Used for troubleshooting. **Never contains your session keys, account labels, `cf_clearance` values, project paths or names, skill names, or session-log contents** — only presence/absence, HTTP status codes, and stack traces. |

### On the Mac

The table above describes the Windows app. The macOS app stores the same two credentials, `sessionKey` and the optional `cf_clearance`, which are sent only to `claude.ai`:

- **Release builds** (signed with the publisher's Developer ID): the macOS Keychain, as generic-password items under service `com.626labs.sanduhr`, readable after first unlock and trusted only to the signed Sanduhr app. An update from 2.3.1 or earlier moves the values out of the old file once, checks the Keychain holds them, then deletes the file; if that fails, the file stays and is used as before.
- **Self-built development builds**: `~/Library/Application Support/Sanduhr/credentials.json`, plaintext, mode `0600` (only your macOS user account can read it).

**Accounts (2.4.0+).** Each named account has its own pair, under the same slot names as Windows: `sessionKey:{Account}` and `cf_clearance:{Account}`. Updating from 2.3.x moves the single saved key to an account called Personal. The list of account labels and which one is active live in the app's preferences (`defaults` keys `accounts` and `activeAccount`), so showing them never touches the Keychain. Each account's usage history is its own file, `~/Library/Application Support/Sanduhr/history.{Account}.json` (the old `history.json` becomes Personal's): the meter percentages with their times, kept for 30 days (rolling, as on Windows) to draw the sparklines, and never sent anywhere. Settings → Accounts → Meter history → Off stops recording an account and offers to erase its file at once; Remove Account deletes it too. Which accounts have it off is kept in the app's preferences (`meterHistoryOff`). Labels are the names you choose; they are shown in the app and used in those slot and file names, and **never written to a log, `snapshot.json` or the debug `state.yaml`**: the snapshot names the active account only by `account_ref`, a short hash of its label. With **Follow the account I'm using** on (off by default), Sanduhr also asks `claude.ai` for the usage of your other signed-in accounts every 15 minutes, each with its own key, the same request as for the active account; the readings stay in memory.

No key is ever written to a log.

## What Sanduhr does NOT do

- **No telemetry.** No install pings, no usage analytics, no crash reporting to any server we control.
- **No advertising.** The app displays no ads and sends no data to ad networks.
- **No third-party SDKs** other than the open-source libraries listed in `windows/requirements.txt` (PySide6, cloudscraper, keyring, requests), which run locally and don't make outbound connections beyond what the widget explicitly asks.
- **No account on our side.** You don't sign up with 626Labs. We have no user database. We cannot identify you.

## Who Sanduhr talks to over the network

By default, exactly one destination:

- `https://claude.ai/api/organizations` — to discover which Claude organization your account belongs to
- `https://claude.ai/api/organizations/{your-org-id}/usage` — to read your subscription usage

These are the same endpoints your browser hits when you visit the claude.ai usage page. Sanduhr acts on your behalf using the cookie you paste in — it is not a separate account or identity.

Anthropic's privacy policy governs what they do with those requests: <https://www.anthropic.com/legal/privacy>.

**Only if you turn on Publish usage** (Settings ▸ Publish usage, off by default, and nothing is sent until you also set an endpoint and tick at least one home) there is a second destination, and you choose it:

- The endpoint URL you enter — any absolute http(s) URL you control — receives the daily per-project token counts described in the table above, authenticated the way you configured (bearer token, a header you name, or no credential).
- If you pick the **626 Labs dashboard** preset, that URL is `https://us-central1-project-626labs.cloudfunctions.net/mcp/api/manage_usage`, authenticated with the key you mint in your own dashboard's Agents panel, and the record goes only to *your* dashboard account.

Nothing about your usage comes back to us. The short-version promise above ("does not send your data to 626Labs") holds for every install where this toggle stays off or points anywhere other than the 626 Labs preset, which is every install until you flip it and pick that preset.

## How you remove your data

- **Recommended: Sign Out.** Open Sanduhr → Settings → Credentials → save with an empty `sessionKey` for the account you want cleared. This deletes that account's Windows Credential Manager entries and its local history file.
- **Clear credentials manually:** Windows Start → Credential Manager → delete entries under service `com.626labs.sanduhr`.
- **Clear local storage:** delete `%APPDATA%\Sanduhr\` and `%LOCALAPPDATA%\Sanduhr\`.
- **Uninstall does not wipe credentials.** Start → Apps & features → Sanduhr für Claude → Uninstall removes the installed app files only. It does **not** delete your Windows Credential Manager entries under `com.626labs.sanduhr`, on either the GitHub (.exe) or Microsoft Store install — sign out first, or delete the entries yourself afterward.
- **Note for Microsoft Store installs:** uninstalling from Apps & features also does **not** remove `%LOCALAPPDATA%\Sanduhr` (Windows leaves per-user app data behind). If you want the usage vault gone after uninstall, delete that folder manually.
- **On the Mac, Sign Out:** Sanduhr → Settings → Accounts → select the account → Sign Out, then confirm. This deletes that account's `sessionKey` and `cf_clearance` from both the Keychain and the credentials file; signing out the active account also stops fetching and marks `snapshot.json` signed out with no usage figures in it. The account stays in the list, and its usage history (`history.{Account}.json`) and your settings stay.
- **On the Mac, Remove Account:** Settings → Accounts → select the account → Remove Account…, then confirm. This signs it out as above, deletes its history file, and drops it from the list. To remove everything, remove every account (or sign out), delete the folder `~/Library/Application Support/Sanduhr/` and run `defaults delete com.626labs.sanduhr` and `defaults delete com.626labs.sanduhr.desk`.
- **On the Mac, by hand:** delete the `com.626labs.sanduhr` items in Keychain Access (release builds) or the folder `~/Library/Application Support/Sanduhr/` (development builds). Moving the app to the Trash removes neither, so sign out first.

## Third-party services Sanduhr does not use

For clarity: **no** Firebase, **no** Google Analytics, **no** Segment, **no** Sentry, **no** Mixpanel, **no** Amplitude, **no** Crashlytics, **no** Rollbar, **no** PostHog, **no** Datadog RUM, **no** any SaaS that would send your activity off your machine.

## Children's privacy

Sanduhr does not knowingly collect information from anyone. The app has no user accounts, no telemetry, and no content uploads. Use of Claude.ai itself is subject to Anthropic's own age policies.

## Changes to this policy

If the data story changes, this file will be updated and the change will be noted in the release `CHANGELOG.md`. Major changes (adding any network destination other than `claude.ai`, adding any telemetry, adding any third-party integration that sees your data) will be called out in release notes.

## Questions

Open an issue: <https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues>
