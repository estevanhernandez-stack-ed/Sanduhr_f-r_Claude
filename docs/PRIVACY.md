# Privacy Policy — Sanduhr für Claude

**Last updated:** 2026-09-13
**Publisher:** 626Labs LLC
**Contact:** [GitHub Issues](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues)

## Short version

Sanduhr für Claude is a **local desktop widget**. It does not run a server, does not have a backend, does not collect analytics, does not call home, and does not send your data to 626Labs or any third party.

The only network destination it contacts is `claude.ai` — and only with the credentials you yourself paste into it — to read your own Claude subscription usage so it can show it to you.

One opt-in exception, off by default: **Publish to 626 Labs** sends a daily per-project token-count summary to *your own* 626 Labs dashboard account, and only for the Claude Code homes you tick. Details in the table and the network section below.

## What data Sanduhr touches

| Data | Where it lives | Who can see it | What Sanduhr does with it |
| --- | --- | --- | --- |
| `sessionKey` (claude.ai cookie you paste in) | Windows Credential Manager, service `com.626labs.sanduhr`, slot `sessionKey:{Account}` per registered account | Only applications running as your Windows user account | Sent only to `claude.ai` when fetching that account's usage. Multi-account installs (v2.2.0+) store one slot per named account. |
| `cf_clearance` (optional Cloudflare cookie you paste in) | Windows Credential Manager, same service, slot `cf_clearance:{Account}` per account | Same as above | Sent only to `claude.ai` when fetching that account's usage |
| Account registry (the list of named accounts and which one is active) | Windows Credential Manager, slots `accounts:list` (JSON array of labels) and `accounts:active` (label string) | Same as above | Used by Sanduhr to route fetches and history writes to the right account. The labels are the names you choose ("Personal", "Work", etc.) — Sanduhr does nothing else with them. |
| Your Claude usage percentages (the numbers the widget displays) | `%APPDATA%\Sanduhr\history.{Account}.json` per account on your machine | Only your Windows user account | Stored for up to 30 days (rolling) so the widget can draw a sparkline and the History tab can chart trends. Never transmitted anywhere. |
| Your local Claude Code usage history (the "vault": one summary row per session (including its subagent transcripts) — project name, day-bucketed token totals per model, skill totals; full folder paths only if the hidden `vault_store_full_paths` setting — off by default — is enabled; never conversation content, never prompts) | `%LOCALAPPDATA%\Sanduhr\vault\` on your machine, one folder per Claude Code home you opt in | Only your Windows user account | Kept **indefinitely — unlike Claude Code's own logs, which Claude Code deletes after ~30 days**. Per-home opt-in at first run; erase any time via Settings ▸ Claude Usage (Erase archive / per-home purge) or by deleting the folder while Sanduhr is not running. Quarantined `.bad` recovery files in the same folder are part of the archive. Never transmitted anywhere. |
| Vault bookkeeping (`checkpoints.json`) | Same vault folder | Same | Hashed log-file identifiers only — no readable paths. Rebuilt automatically if deleted. |
| Usage snapshot for the Claude Code statusline (opt-in; exists only while the integration is installed) | `%APPDATA%\Sanduhr\snapshot.json` | Only your Windows user account — read by the statusline script Claude Code runs as you | Percentages, reset times, plan name, and a short account *hash* — **never the account label itself, never keys**. Rewritten each fetch, deleted on account switch, sign-out, or Remove. One caveat: what the statusline renders appears in your terminal like any other on-screen text. Never transmitted anywhere. |
| Statusline registration (opt-in, chosen home only) | The statusline script at `%APPDATA%\Sanduhr\bin\`, plus a `statusLine` entry in the **one** Claude Code home's `settings.json` you pick at install (a timestamped backup is saved beside it first) | Your Windows user account | Lets Claude Code render your caps under its prompt. "Remove statusline" in Settings ▸ Claude Usage reverts the entry (only if it still points at Sanduhr's script), deletes the script, and deletes the snapshot. |
| **Publish to 626 Labs** (opt-in, off by default; every Claude Code home is unshared until you tick it) | Sent to the 626 Labs dashboard (`us-central1-project-626labs.cloudfunctions.net`) once a day at the time you set, or when you click "Publish now" / an agent calls the `publish_usage` MCP tool | Your 626 Labs dashboard account (the agent key you paste identifies it) | One record per day and machine: the date, your machine name, which homes you ticked, one token count per project **name** (folder basename only, never the path) from those homes' local Claude Code logs, a per-tier split, and the widget's current quota percentages (or an explicit stale/no-data marker). Never session content, prompts, account labels, or keys. Re-publishing a day replaces that day's record. Turn it off in Settings ▸ 626 Labs; untick a home to stop sharing it. |
| 626 Labs agent key (the key you paste for publishing) | Windows Credential Manager, service `com.626labs.sanduhr`, slot `626labs:agentKey` | Only applications running as your Windows user account | Sent only to the 626 Labs dashboard as a bearer token when publishing. Never written to a file; `settings.json` records only whether a key is present. "Clear key" in Settings ▸ 626 Labs deletes it. |
| Publish handoff files (only while the `publish_usage` MCP tool is in use) | `%APPDATA%\Sanduhr\publish-request.json` / `publish-result.json` | Your Windows user account | A request id + date, and the upload's status/message. No usage data, no key. Cleared by the widget after each request; a request expires after 24 hours. |
| Your theme preference and last window position | `%APPDATA%\Sanduhr\settings.json` | Your Windows user account | Read at startup to restore your setup |
| Operational logs | `%APPDATA%\Sanduhr\sanduhr.log` (rotating, 1 MB × 3 files) | Your Windows user account | Used for troubleshooting. **Never contains your session keys, account labels, `cf_clearance` values, project paths or names, skill names, or session-log contents** — only presence/absence, HTTP status codes, and stack traces. |

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

**Only if you turn on Publish to 626 Labs** (Settings ▸ 626 Labs, off by default) there is a second destination:

- `https://us-central1-project-626labs.cloudfunctions.net/mcp/api/manage_usage` — to record the daily per-project token counts described in the table above, authenticated with the agent key you paste in.

This is the one case where data goes to 626 Labs, and it goes only to *your* dashboard account. The short-version promise above ("does not send your data to 626Labs") holds for every install where this toggle stays off, which is every install until you flip it.

## How you remove your data

- **Recommended: Sign Out.** Open Sanduhr → Settings → Credentials → save with an empty `sessionKey` for the account you want cleared. This deletes that account's Windows Credential Manager entries and its local history file.
- **Clear credentials manually:** Windows Start → Credential Manager → delete entries under service `com.626labs.sanduhr`.
- **Clear local storage:** delete `%APPDATA%\Sanduhr\` and `%LOCALAPPDATA%\Sanduhr\`.
- **Uninstall does not wipe credentials.** Start → Apps & features → Sanduhr für Claude → Uninstall removes the installed app files only. It does **not** delete your Windows Credential Manager entries under `com.626labs.sanduhr`, on either the GitHub (.exe) or Microsoft Store install — sign out first, or delete the entries yourself afterward.
- **Note for Microsoft Store installs:** uninstalling from Apps & features also does **not** remove `%LOCALAPPDATA%\Sanduhr` (Windows leaves per-user app data behind). If you want the usage vault gone after uninstall, delete that folder manually.

## Third-party services Sanduhr does not use

For clarity: **no** Firebase, **no** Google Analytics, **no** Segment, **no** Sentry, **no** Mixpanel, **no** Amplitude, **no** Crashlytics, **no** Rollbar, **no** PostHog, **no** Datadog RUM, **no** any SaaS that would send your activity off your machine.

## Children's privacy

Sanduhr does not knowingly collect information from anyone. The app has no user accounts, no telemetry, and no content uploads. Use of Claude.ai itself is subject to Anthropic's own age policies.

## Changes to this policy

If the data story changes, this file will be updated and the change will be noted in the release `CHANGELOG.md`. Major changes (adding any network destination other than `claude.ai`, adding any telemetry, adding any third-party integration that sees your data) will be called out in release notes.

## Questions

Open an issue: <https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues>
