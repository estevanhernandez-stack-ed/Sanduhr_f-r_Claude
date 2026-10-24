## Sanduhr für Claude 3.9 (Windows)

**Download:** [`626Labs.Sanduhr-win-Setup.exe`](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v3.9.0/626Labs.Sanduhr-win-Setup.exe) (per-user install). **On 3.8, you don't need to download anything:** Sanduhr fetches this update in the background and offers **Restart to update** when it is ready. A 3.7.x Setup.exe install needs this Setup.exe once. **Microsoft Store installs update through the Store.** The `.msix` on this page is an unsigned build artifact and will not install by double-click; see [INSTALL.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/INSTALL.md).

### What's new

- **CI watchers.** Settings ▸ Desk ▸ **Show my CI runners' jobs** (off by default) puts a card on the Desk for every job a GitHub Actions runner on your PC runs, however the job started: a push, a pull request or a re-run on github.com. A passed job fades after a few seconds, a failed one stays until you dismiss it and glows once, and a canceled or skipped one fades as finished. The companion cheers a job that passes.
- **Local only.** Sanduhr reads the runner containers' log output through Docker's local pipe. No GitHub account, no token, no network. It lists your containers and follows those logs, and makes no other Docker call, so a runner's registration token is never read. Runners are found by image name, so ones you add later show up without setup.
- **A status line says what it sees.** Under the switch: which runners it is watching, or that no runner containers are running, that Docker isn't running, that Docker answered but refused the request, or that your Windows account needs to be in the docker-users group. Runners scaled from one Compose service each get their own card, shown by container name.
- **Built for long builds.** A runner is silent while a job runs, so its card never goes "lost touch." Open Sanduhr mid-job and the card shows the job's real start time. Runner cards hide in demo mode.

This watches runners on the PC Sanduhr runs on, and needs Docker running there. Claude Code's own watchers (`watch_start`) work as before.

The full list is in the changelog.

### Known issues

- Signing in with a Google account inside the sign-in window still dead-ends (Google blocks embedded OAuth). Use **Continue with email** on the claude.ai page and enter the code Anthropic emails you; the manual session-key paste remains under Settings ▸ Accounts.
- Store installs: if the update won't install (error 0x80073D02), restart Windows and try again.
- Setup.exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first install: More info → Run anyway. The Microsoft Store package is the signed path.

Full changelog: [CHANGELOG.md](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/CHANGELOG.md#v390--2026-10-24)

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively. Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, or associated with Anthropic PBC.
