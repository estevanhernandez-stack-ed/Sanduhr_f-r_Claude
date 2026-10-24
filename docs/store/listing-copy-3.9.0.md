# Store listing copy — v3.9.0 (shipped)

> Archive of what went to the Store for 3.9.0.0 (certified 2026-10-24 on the first try, submission
> 1152921505702221950, created and committed through the console API; the MSIX and the reviewer
> letter went in by hand in Partner Center). Source of truth is
> `store-listing-console/apps/sanduhr/copy/` (#46, 107701b); this file is the record of what
> shipped, per release-runbook Phase 7 step 8.
>
> Only the what's-new field changed against 3.8.0.0 (`submit_write.py plan`: `releaseNotes`,
> 1170 to 1017 characters). The description, features, keywords and screenshots are 3.8.0.0's,
> unchanged.

---


## English

```
Version 3.9.0

- CI watchers: turn on Settings > Desk > Show my CI runners' jobs and
  every job a GitHub Actions runner on this PC runs gets a card on your
  Desk. A passed job fades, a failed one stays and glows once.
- Local only, and off by default. Sanduhr reads the runner containers'
  log output through Docker's pipe on this PC: no GitHub account, no
  token, no network. It lists containers and follows those logs, and
  makes no other Docker call.
- A status line under the switch names the runners it watches, or
  says why there are none (Docker not running, or your account needs
  the docker-users group).
- Open Sanduhr mid-job and the card shows the job's real start time.
  Runner cards hide in demo mode.

Known issue: if the update won't install (error 0x80073D02), restart
Windows and try again.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with, endorsed by, or associated with Anthropic PBC.
```
