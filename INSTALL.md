# Install guide — Sanduhr für Claude (Windows)

This is the guide for the shipping Windows app (.NET 10 / WPF, `windows-dotnet/`). macOS users:
see [mac/README.md](mac/README.md). The original Python widgets were retired and removed on 2026-09-13
(preserved at tag `legacy/python-v2.3.0`).

## Requirements

- Windows 10 version 1809 (build 17763) or later, x64. Windows 11 22H2+ gets the Mica glass
  backdrop; Windows 10 falls back to a solid theme color.
- The WebView2 runtime (part of Windows 11; on Windows 10 it usually arrives with Edge). Without
  it, the in-app sign-in is unavailable and the manual session-key path below still works.
- An active Claude Pro, Max, Team, or Enterprise subscription. Sanduhr reads your own usage from
  claude.ai; it has no account of its own.

## Install

### 1. Microsoft Store (recommended)

Open the Microsoft Store and search for **Sanduhr für Claude** (publisher 626Labs LLC), then
Install. The Store signs the package, so there is no SmartScreen prompt, and updates arrive through
the Store like any other app.

### 2. GitHub Release (Setup.exe)

Download **`626Labs.Sanduhr-win-Setup.exe`** from the
[latest release](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/latest)
and run it.

- The installer is not code-signed, so SmartScreen shows "Windows protected your PC": click
  **More info → Run anyway**. The Store package is the signed path if you would rather not.
- It installs per-user under `%LOCALAPPDATA%\626Labs.Sanduhr` and checks the release feed for
  updates; new versions install themselves within about a day of publishing.
- `626Labs.Sanduhr-win-Portable.zip` on the same release page is the no-install flavor.

### Sideloading the `.msix`

The `Sanduhr-Store-v<version>.msix` attached to a GitHub Release is the **unsigned** package that
gets uploaded to Partner Center; the Store signs it on ingestion. Windows will not install it as
is. Use the Store or the Setup.exe. (Developers: `windows-dotnet/scripts/build-msix.ps1 -Sideload`
builds a self-signed flavor for local testing; see `docs/release-runbook.md`.)

## First run: sign in

1. Launch Sanduhr and click **Sign in to Claude**. A window opens on the real `claude.ai` login
   page; Sanduhr never sees your password, only the session cookie claude.ai sets in the app's own
   cookie jar.
2. **Google account?** Google blocks its sign-in inside embedded windows. Choose **Continue with
   email** on the claude.ai page, enter your Gmail address, and type the one-time code Anthropic
   emails you. Email + password and passkeys work directly.
3. Sanduhr stores the session in Windows Credential Manager and starts fetching. More accounts
   (Personal + Work) go in under **Settings → Accounts**.

**Manual session key** (fallback, or when WebView2 is missing): sign in to
[claude.ai](https://claude.ai) in your browser, open DevTools (`F12`) → **Application → Cookies
→ claude.ai**, copy the `sessionKey` value, then **Settings → Accounts → Add by sessionKey**.

**Session expired?** The widget shows a one-click **Sign in again** card that re-authenticates the
account in place; your history is kept.

## Where your data lives

Everything is on your machine, under your Windows user account. The full data table is in
[docs/PRIVACY.md](docs/PRIVACY.md).

| What | Where |
| --- | --- |
| Session cookies, account list, optional publish token | Windows Credential Manager, service `com.626labs.sanduhr` (`sessionKey:{Account}`, `cf_clearance:{Account}`, `accounts:list`, `accounts:active`, `publish:token`) |
| Settings, theme, window position, alert thresholds, publish destination | `%APPDATA%\Sanduhr\settings.json` |
| 30-day usage history per account | `%APPDATA%\Sanduhr\history.{Account}.json` |
| User themes and sound drop-ins | `%APPDATA%\Sanduhr\themes\`, `%APPDATA%\Sanduhr\sounds\` |
| Claude Code statusline snapshot + helper script (opt-in) | `%APPDATA%\Sanduhr\snapshot.json`, `%APPDATA%\Sanduhr\bin\` |
| Claude Code usage vault (opt-in, kept until you erase it) | `%LOCALAPPDATA%\Sanduhr\vault\` |
| Operational log (no keys, labels, or paths) | `%APPDATA%\Sanduhr\sanduhr.log` |

Only `claude.ai` is contacted, with your own cookie. **Publish usage** (Settings ▸ Publish usage)
is off by default and posts a daily per-project token summary only to an endpoint you configure.

## Update

- **Store install:** updates through the Microsoft Store.
- **Setup.exe install:** Sanduhr checks the GitHub release feed and updates itself; you can also
  run the newer Setup.exe over the existing install.

## Uninstall

**Sign out first if you want your credentials gone.** Settings → Credentials → save with an empty
`sessionKey` (per account) deletes that account's Credential Manager entries and its history file.

Then **Start → Settings → Apps → Installed apps → Sanduhr für Claude → Uninstall**, on either
channel. Per [docs/PRIVACY.md](docs/PRIVACY.md):

- Uninstall removes the installed app files only. It does **not** delete the Credential Manager
  entries under `com.626labs.sanduhr`, on either the Store or the Setup.exe install. Sign out
  first, or remove them yourself: Start → Credential Manager → Windows Credentials.
- A Store uninstall also leaves `%LOCALAPPDATA%\Sanduhr` (the usage vault) behind. Delete
  `%APPDATA%\Sanduhr\` and `%LOCALAPPDATA%\Sanduhr\` by hand if you want every trace gone.
- If you installed the Claude Code statusline, use **Remove statusline** in Settings ▸ Claude
  Usage before uninstalling; it reverts the `settings.json` entry in your Claude Code home.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| "Session expired" | Click **Sign in again** on the card, or Settings → Accounts → Update sign-in |
| Google sign-in dead-ends in the window | Use **Continue with email** and the emailed code (Google blocks embedded OAuth) |
| Cloudflare challenge / "Blocked" status | Wait for the automatic re-navigation; if it persists, sign in again or paste a fresh `cf_clearance` under Settings → Accounts |
| No Mica glass | Windows 10 or Win11 pre-22H2: solid theme color is the expected fallback |
| No tiers showing | Your subscription has no usage in the current window yet; wait for the next fetch (every 5 min, `Ctrl+R` to force) |

Keyboard shortcuts and every control are listed in-app under **Settings → Help**.

---

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively to describe
integration. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed
by, or associated with Anthropic PBC.
