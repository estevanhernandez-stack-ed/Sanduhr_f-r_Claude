# Privacy Policy — Sanduhr für Claude

**Last updated:** 2026-07-13
**Publisher:** 626Labs LLC
**Contact:** [GitHub Issues](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues)

## Short version

Sanduhr für Claude is a **local desktop widget**. It does not run a server, does not have a backend, does not collect analytics, does not call home, and does not send your data to 626Labs or any third party.

The only network destination it contacts is `claude.ai` — and only with the credentials you yourself paste into it — to read your own Claude subscription usage so it can show it to you.

## What data Sanduhr touches

| Data | Where it lives | Who can see it | What Sanduhr does with it |
| --- | --- | --- | --- |
| `sessionKey` (claude.ai cookie you paste in) | Windows Credential Manager, service `com.626labs.sanduhr`, slot `sessionKey:{Account}` per registered account | Only applications running as your Windows user account | Sent only to `claude.ai` when fetching that account's usage. Multi-account installs (v2.2.0+) store one slot per named account. |
| `cf_clearance` (optional Cloudflare cookie you paste in) | Windows Credential Manager, same service, slot `cf_clearance:{Account}` per account | Same as above | Sent only to `claude.ai` when fetching that account's usage |
| Account registry (the list of named accounts and which one is active) | Windows Credential Manager, slots `accounts:list` (JSON array of labels) and `accounts:active` (label string) | Same as above | Used by Sanduhr to route fetches and history writes to the right account. The labels are the names you choose ("Personal", "Work", etc.) — Sanduhr does nothing else with them. |
| Your Claude usage percentages (the numbers the widget displays) | `%APPDATA%\Sanduhr\history.{Account}.json` per account on your machine | Only your Windows user account | Stored for up to 30 days (rolling) so the widget can draw a sparkline and the History tab can chart trends. Never transmitted anywhere. |
| Your local Claude Code usage history (the "vault": one summary row per session (including its subagent transcripts) — project name, day-bucketed token totals per model, skill totals; full folder paths only if the hidden `vault_store_full_paths` setting — off by default — is enabled; never conversation content, never prompts) | `%LOCALAPPDATA%\Sanduhr\vault\` on your machine, one folder per Claude Code home you opt in | Only your Windows user account | Kept **indefinitely — unlike Claude Code's own logs, which Claude Code deletes after ~30 days**. Per-home opt-in at first run; erase any time via Settings ▸ Claude Usage (Erase archive / per-home purge) or by deleting the folder while Sanduhr is not running. Quarantined `.bad` recovery files in the same folder are part of the archive. Never transmitted anywhere. |
| Vault bookkeeping (`checkpoints.json`) | Same vault folder | Same | Hashed log-file identifiers only — no readable paths. Rebuilt automatically if deleted. |
| Your theme preference and last window position | `%APPDATA%\Sanduhr\settings.json` | Your Windows user account | Read at startup to restore your setup |
| Operational logs | `%APPDATA%\Sanduhr\sanduhr.log` (rotating, 1 MB × 3 files) | Your Windows user account | Used for troubleshooting. **Never contains your session keys, account labels, `cf_clearance` values, project paths or names, skill names, or session-log contents** — only presence/absence, HTTP status codes, and stack traces. |

## What Sanduhr does NOT do

- **No telemetry.** No install pings, no usage analytics, no crash reporting to any server we control.
- **No advertising.** The app displays no ads and sends no data to ad networks.
- **No third-party SDKs** other than the open-source libraries listed in `windows/requirements.txt` (PySide6, cloudscraper, keyring, requests), which run locally and don't make outbound connections beyond what the widget explicitly asks.
- **No account on our side.** You don't sign up with 626Labs. We have no user database. We cannot identify you.

## Who Sanduhr talks to over the network

Exactly one destination:

- `https://claude.ai/api/organizations` — to discover which Claude organization your account belongs to
- `https://claude.ai/api/organizations/{your-org-id}/usage` — to read your subscription usage

These are the same endpoints your browser hits when you visit the claude.ai usage page. Sanduhr acts on your behalf using the cookie you paste in — it is not a separate account or identity.

Anthropic's privacy policy governs what they do with those requests: <https://www.anthropic.com/legal/privacy>.

## How you remove your data

- **Clear credentials only:** Windows Start → Credential Manager → delete entries under service `com.626labs.sanduhr`. Or: open Sanduhr → Settings → clear the field and save.
- **Clear local storage:** delete `%APPDATA%\Sanduhr\` and `%LOCALAPPDATA%\Sanduhr\`.
- **Full uninstall + data wipe:** Start → Apps & features → Sanduhr für Claude → Uninstall, and check "Also remove my settings and history" on the uninstall dialog. Credential Manager entries are cleared automatically on uninstall regardless of that checkbox.
- **Note for Microsoft Store installs:** uninstalling from Apps & features does **not** remove `%LOCALAPPDATA%\Sanduhr` (Windows leaves per-user app data behind). If you want the usage vault gone after uninstall, delete that folder manually.

## Third-party services Sanduhr does not use

For clarity: **no** Firebase, **no** Google Analytics, **no** Segment, **no** Sentry, **no** Mixpanel, **no** Amplitude, **no** Crashlytics, **no** Rollbar, **no** PostHog, **no** Datadog RUM, **no** any SaaS that would send your activity off your machine.

## Children's privacy

Sanduhr does not knowingly collect information from anyone. The app has no user accounts, no telemetry, and no content uploads. Use of Claude.ai itself is subject to Anthropic's own age policies.

## Changes to this policy

If the data story changes, this file will be updated and the change will be noted in the release `CHANGELOG.md`. Major changes (adding any network destination other than `claude.ai`, adding any telemetry, adding any third-party integration that sees your data) will be called out in release notes.

## Questions

Open an issue: <https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues>
