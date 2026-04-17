# Privacy Policy — Sanduhr für Claude

**Last updated:** 2026-04-16
**Publisher:** 626Labs LLC
**Contact:** [GitHub Issues](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues)

## Short version

Sanduhr für Claude is a **local desktop widget**. It does not run a server, does not have a backend, does not collect analytics, does not call home, and does not send your data to 626Labs or any third party.

The only network destination it contacts is `claude.ai` — and only with the credentials you yourself paste into it — to read your own Claude subscription usage so it can show it to you.

## What data Sanduhr touches

| Data | Where it lives | Who can see it | What Sanduhr does with it |
| --- | --- | --- | --- |
| `sessionKey` (claude.ai cookie you paste in) | Windows Credential Manager, service `com.626labs.sanduhr` | Only applications running as your Windows user account | Sent only to `claude.ai` when fetching your usage |
| `cf_clearance` (optional Cloudflare cookie you paste in) | Windows Credential Manager, same service | Same as above | Sent only to `claude.ai` when fetching your usage |
| Your Claude usage percentages (the numbers the widget displays) | `%APPDATA%\Sanduhr\history.json` on your machine | Only your Windows user account | Stored for up to 2 hours (24 rolling snapshots) so the widget can draw a sparkline. Never transmitted anywhere. |
| Your theme preference and last window position | `%APPDATA%\Sanduhr\settings.json` | Your Windows user account | Read at startup to restore your setup |
| Operational logs | `%APPDATA%\Sanduhr\sanduhr.log` (rotating, 1 MB × 3 files) | Your Windows user account | Used for troubleshooting. **Never contains your session key or `cf_clearance` value** — only presence/absence, HTTP status codes, and stack traces. |

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
- **Clear local storage:** delete `%APPDATA%\Sanduhr\`.
- **Full uninstall + data wipe:** Start → Apps & features → Sanduhr für Claude → Uninstall, and check "Also remove my settings and history" on the uninstall dialog. Credential Manager entries are cleared automatically on uninstall regardless of that checkbox.

## Third-party services Sanduhr does not use

For clarity: **no** Firebase, **no** Google Analytics, **no** Segment, **no** Sentry, **no** Mixpanel, **no** Amplitude, **no** Crashlytics, **no** Rollbar, **no** PostHog, **no** Datadog RUM, **no** any SaaS that would send your activity off your machine.

## Children's privacy

Sanduhr does not knowingly collect information from anyone. The app has no user accounts, no telemetry, and no content uploads. Use of Claude.ai itself is subject to Anthropic's own age policies.

## Changes to this policy

If the data story changes, this file will be updated and the change will be noted in the release `CHANGELOG.md`. Major changes (adding any network destination other than `claude.ai`, adding any telemetry, adding any third-party integration that sees your data) will be called out in release notes.

## Questions

Open an issue: <https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/issues>
