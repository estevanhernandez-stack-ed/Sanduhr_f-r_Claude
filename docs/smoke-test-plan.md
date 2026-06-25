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
