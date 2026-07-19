# Smoke-test plan — login & key-entry recovery hardening

> Manual GUI verification for the things builds and unit tests can't prove (real
> `claude.ai` login, WebView2 runtime behavior, the actual recovery UX). Run on a
> Windows 11 machine with the WebView2 Evergreen runtime. Pairs with the spec
> [`docs/superpowers/specs/2026-06-22-login-recovery-hardening-design.md`](superpowers/specs/2026-06-22-login-recovery-hardening-design.md)
> and plan [`docs/superpowers/plans/2026-06-22-login-recovery-hardening.md`](superpowers/plans/2026-06-22-login-recovery-hardening.md).
>
> **Run:** `dotnet run --project windows-dotnet/src/Sanduhr.App` (or launch the built
> `Sanduhr.exe`). Tray-resident — left-click the tray icon to show the widget.
> **Status:** ✅ pass · ❌ fail · ⬜ not yet run.

## Setup helpers

- **Force an expired session:** with a signed-in `Personal` account, open **Credential Manager → Windows Credentials**, find `sessionKey:Personal@com.626labs.sanduhr`, and overwrite the value with garbage (or sign out of claude.ai in a browser to let it expire naturally). Then trigger a refresh (`Ctrl+R` / tray → Refresh).
- **Simulate WebView2 missing:** test on a machine / fresh Windows image without the Evergreen runtime, or temporarily rename the runtime folder. (Don't uninstall the shared runtime on a working dev box.)
- **Simulate a slow/blocked first load:** disconnect the network before clicking "Sign in to Claude".
- **Legacy upgrader:** seed a bare `sessionKey@com.626labs.sanduhr` Credential Manager slot (no `accounts:list` / `accounts:active`) to mimic a pre-v2.2.0 install, then launch.
- **Diagnostics:** `%APPDATA%\Sanduhr\signin-debug.log` records presence booleans + hosts only (never secret values) — useful when a capture doesn't fire.

---

## Tier 1 — recovery core (built)

- ⬜ **T1-a · First run, no DevTools.** Fresh install (no credentials) → widget shows the **"Track your Claude usage"** card with **"Sign in to Claude"**. Click it → secure window opens on `claude.ai/login` → sign in → window closes itself → cards populate. No DevTools, no paste.
- ⬜ **T1-b · Expired → one-click in-place recovery (the headline).** Force an expired session → refresh → card flips to **"Session expired — sign in again"** (an actual button, not a status line). Click → secure window → re-login → the **same `Personal`** account refreshes, `history.Personal.json` is intact, and **no "Account 2"** appears in Settings → Accounts.
- ⬜ **T1-c · Cloudflare challenge → recovery.** If a `CloudflareBlockedException` occurs (403), the card reads **"Connection challenged — sign in again"** and the same re-auth refreshes `cf_clearance` silently. (Opportunistic — hard to force on demand; watch for it.)
- ⬜ **T1-d · Legacy upgrader keeps their account.** Seed a pre-v2.2.0 bare `sessionKey` slot → launch → account is promoted to `Personal` and tracking starts; the first-run card does **not** appear.
- ⬜ **T1-e · Manual paste still works.** Card → **"Paste a key instead"** → `ManualKeyWindow` accepts a pasted `sessionKey` and tracks. (Power-user fallback intact.)
- ⬜ **T1-f · UA sanity (spec pre-flight #2).** Watch the live sign-in + first fetch. If sign-in succeeds but fetches immediately throw `CloudflareBlockedException`, the hardcoded `ClaudeSignIn.BrowserUserAgent` (`Chrome/131`) may have drifted — a one-line constant bump.

---

## Tier 2 — embedded window hardening (built)

- ⬜ **T2-a · Slow/blocked load → timeout + escape hatch.** Disconnect network → "Sign in to Claude" → after the timeout the loading overlay becomes an error panel with **"Try again"** + **"Paste a key instead"**; the latter reaches `ManualKeyWindow` (the coordinator's Failed→manual branch fires — proving load-failure is distinct from user-cancel).
- ⬜ **T2-b · First-load failure (not a hang).** Force a failed first navigation → error panel appears instead of an indefinite "Loading claude.ai…".
- ⬜ **T2-c · User-cancel still cancels.** Close the sign-in window with the X before signing in → no error, no manual-paste prompt; widget stays on its card.
- ⬜ **T2-d · WebView2 missing → install → retry.** Runtime absent → modal offers Install / Paste / Learn More. Click **Install Now**, install the runtime, click **Retry** → embedded flow re-enters (no dead-end, no forced manual paste).
- ⬜ **T2-e · Name an added account.** With one account already active, tray → **Add account** → embedded sign-in → prompted for a name (default offered) → the new account appears under that name, not "Account 2".

---

## Tier 3 — manual fallback polish (built)

- ⬜ **T3-a · Help affordance.** `ManualKeyWindow` → the DevTools steps are tucked behind a collapsed **"Where do I find this?"**, not shouted in the subtitle.
- ⬜ **T3-b · Bounce to embedded.** With WebView2 available, `ManualKeyWindow` → **"Use the secure sign-in window instead"** → closes the modal and opens the embedded flow. (Hidden when WebView2 is absent.)

---

## Regression sweep (run after Tier 3)

- ⬜ Account switch, rename, sign-out still behave (no recovery-state bleed).
- ⬜ A normal fetch after recovery clears the card (`Reason = None`) and renders tiers.
- ⬜ `dotnet test windows-dotnet/Sanduhr.slnx` green; full solution builds 0/0.

---

## WS-A — auth & accounts overhaul (2026-07-11)

Theming rule for every scenario below: run once in the default theme, then flip through one dark, one light, and Matrix with the surface open — zero unstyled or stale-colored elements, live re-tint included.

1. **Manual-origin expiry → paste-primary card.** Add an account via "Paste a key instead" with a deliberately bogus sessionKey. Wait for the fetch to 401. Expect the recovery card to lead with "Paste a new key" (NOT "Sign in again"); the secondary link reads "Use browser sign-in instead". Primary opens the Update sessionKey modal with the account name locked.
2. **Embedded-origin expiry unchanged.** For a browser-signed-in account with a dead session, the card still leads with "Sign in again"; secondary reads "Paste a key instead" and opens the paste modal IN PLACE (see 3).
3. **No duplicate accounts from recovery paste.** From an Expired card, use the paste path and save a key. Expect: same account label, no new "Account N" in Settings ▸ Accounts, history intact.
4. **Settings ▸ Update sign-in, non-active account.** With two accounts, select the non-active one → "Update sign-in…". Expect the flow matching that account's origin, no active-account switch, and no fetcher hiccup on the active account.
5. **Remove account is complete.** Remove a signed-in account. Expect: gone from the list, `history.{label}.json` gone from `%APPDATA%\Sanduhr`, confirm-dialog text matches what actually happened.
6. **Last-account removal purges transport cookies.** Remove the only account. Expect the widget to drop to first-run, and `%APPDATA%\Sanduhr\webview2-fetch` to be deleted (or, if locked, a line in %APPDATA%\Sanduhr\sanduhr.log about the deferred purge).
7. **Rename carries history.** Rename an account with a visible history chart. Expect the chart intact under the new name and no `history.{old}.json` left behind.
8. **First-run unchanged.** Fresh install: primary "Sign in to Claude", secondary "Paste a key instead" ADDS the account (no reauth semantics).

---

## WS-B — threshold alerts (2026-07-12)

1. **Warn crossing.** Set Warn to a value just below a live tier's current %, wait for the next fetch (or click Refresh). Expect one toast naming the tier and % plus the soft two-note chime; no repeat on subsequent fetches.
2. **Urgent supersedes.** Set both thresholds below the live %, refresh. Expect a single Urgent toast (not two), replacing any prior toast for that tier in Action Center.
3. **Test button.** Settings ▸ Alerts ▸ Send test alert. Expect a Warn-style toast + chime regardless of thresholds.
4. **Focus Assist.** Enable Do Not Disturb, send a test alert. Expect: no audible chime; the toast lands in Action Center silently (deferred by Windows).
5. **Snake.** Enable "100% plays the !", send... nothing — the sting only binds to a real Full event. Verify by temporarily setting Warn/Urgent near a tier at 100% (or accept this as covered by the ChimeSynth unit tests plus scenario 3's pipeline coverage; the sting WAV can be auditioned by toggling the setting and hitting 100% naturally).
6. **Validation.** Set Warn 95 / Urgent 80. Expect the themed inline hint and no persistence (reopen Settings to confirm the old values).
7. **Recovery suspension.** Break the session (bogus key), confirm recovery card, then reauth. Expect no alert storm from the recovery/re-auth cycle.
8. **Velopack channel toast.** On an unpackaged (GitHub) install, send a test alert — the AUMID compat path must show a toast with the Sanduhr name/icon.
9. **Theming.** Flip default/dark/light/Matrix with the Alerts tab open — zero unstyled elements.
10. **Toast click focuses the widget.** Send a test alert, click the toast body: the widget window comes to the foreground — verify on BOTH channels, with the app running. On the MSIX channel, also verify no second Sanduhr instance appears (Task Manager) after the click.
11. **MSIX sideload toast.** On a -Sideload MSIX build, send a test alert: toast shows with the Sanduhr name/icon, and clicking it activates (not relaunches) the app. Gate for the next Store submission.

---

## WS-C — usage vault + Claude Usage tab (2026-07-12)

Theming rule as above: run once in the default theme, then flip dark / light / Matrix with the surface open — zero unstyled elements (hatch + no-record textures included).

1. **First-run consent.** Fresh settings.json (`vault_prompted` absent): launch shows the themed per-home consent dialog once, pre-checked. "Keep history" → `%LOCALAPPDATA%\Sanduhr\vault\.claude*\sessions-*.json` appear within ~1 min. Relaunch: no re-prompt.
2. **Not now is honored.** Decline the dialog: no vault folder appears, ever; Overview falls back to live logs with no status line; Sessions shows the vault-off empty state.
3. **Overview parity.** With the vault fresh, Overview's Today / Last 30 days match the pre-WS-C numbers (within one 30s refresh of each other).
4. **Degraded honesty.** Stop ingestion (Task Manager: suspend the app > 15 min, or temporarily set the machine clock forward): Overview shows "history vault paused — showing live logs only" and live numbers. Resume: line clears within a cycle.
5. **Ledger answers "what ate 800k yesterday".** Sessions ▸ Yesterday chip: token column shows yesterday-only burn, top row is yesterday's heaviest session, expansion shows its per-day/model breakdown.
6. **Scroll + expansion survive refresh.** Expand a row, scroll mid-list, wait 5+ min (an ingest cycle): scroll position and the expanded row survive.
7. **Two processes, no clobber.** Run the Store build and a Velopack/debug build simultaneously for 10+ min: `sanduhr.log` shows "ingest skipped (writer mutex held)" lines from one side; no `.bad` files; session totals stay correct.
8. **Erase archive is real.** Settings ▸ Claude Usage ▸ Erase archive → confirm: vault folder empties, all root checkboxes untick, and NO files reappear over the next 10 min (consent tombstone holds).
9. **Per-root purge.** Untick one home → choose erase: that folder is gone, the other home's folder untouched; re-tick: backfill restores it within a cycle.
10. **Trends honesty.** On a fresh vault, Trends shows ~4 seeded weeks; earlier weeks show the dotted no-record texture (not zero bars); current week hatched; footer names the birth date.
11. **Privacy spot-check.** Open `sanduhr.log` after a full session: no paths, no project names, no skill names, no JSONL content. Open `checkpoints.json`: hex keys only.
12. **MSIX virtualization re-check.** On the Store/MSIX build, confirm vault writes land at the REAL `%LOCALAPPDATA%\Sanduhr\vault` (spike verified virtualization off on 3.1.0 — re-verify on this package build).

---

## WS-C.1 — subagent coverage, split, chip, calendar (2026-07-13)

1. **The jump.** First launch on this build: within a cycle, Overview's 30-day total rises sharply (subagent transcripts now count) and `sanduhr.log` shows one "walk upgraded" line per home. Second launch: no further jump, no repeated upgrade line.
2. **Ledger folds agents.** A subagent-heavy session shows ONE ledger row; its expansion carries "Agents: N · X tokens"; the flat list has no agent-* rows.
3. **Split lines.** Overview shows "↑ … sent · ↓ … received" under both figures; the 30-day line carries "(partial)" while pre-upgrade days remain in the window, and today's line never does.
4. **Calendar honesty.** 5-week grid below the strip: heat where there's history, dotted no-record texture on uncovered days, faint tick on covered-zero days, today outlined; hover names the day + count ("no record" on textured cells). Vault off → all texture.
5. **% chip.** Widget tier cards: the % sits on a chip and reads clearly over BOTH sparkline styles in default/dark/light/Matrix.
6. **Regression trio.** Stack toggle still stacks; erase dialogs still say "Erase it / Keep data"; the paused/off status lines still show in their states.

---

## Scoped-limits wave (2026-07-19)

1. **The Fable bar.** With live data on a Max account: a "Weekly - Fable" card renders
   between Weekly - Opus and the rest, percentage matching claude.ai; it appears in the
   Settings hide/reorder list, the History chart tier rows, and CSV export headers.
2. **July 20 flip.** After the entitlement change lands upstream, the bar tracks the new
   50% standard allocation with no app update.
3. **Unknown-key log.** fetch-debug.log contains exactly one `usage: unregistered keys:`
   line naming the null codename buckets (tangelo, …) per app session — not one per cycle.
4. **Org stability.** Accounts with a claude_max org + an API org track the Max org's
   usage regardless of API-side org ordering (numbers match claude.ai's settings page).
5. **CC attribution.** Local Claude Code burn on a fable model shows as the Fable card's
   `+Nk` badge, not only in the footer total.
