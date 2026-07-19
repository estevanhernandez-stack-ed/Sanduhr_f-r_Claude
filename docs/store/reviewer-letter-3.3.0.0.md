# Notes for certification — reviewer letter (v3.3.0.0)

> Paste the block between the `---` markers into Partner Center → Submission options → Notes
> for certification (~2000-char cap; front-loaded per playbook gotcha #8).
>
> Framing: v3.2.0.0 is the approved current version. v3.3.0 has **no disclosure-surface
> change** — same endpoints, same at-rest data, same consent surfaces — so the letter says so
> explicitly up front (runbook Phase 2 rule) and summarizes the feature deltas briefly.

---

```
Hello reviewer,

This updates the approved v3.2.0.0 — same Identity
(626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing. There is NO
disclosure-surface change in this release: no new endpoints, no new at-rest
data, no permission changes, no change to the opt-in local usage vault you
reviewed in 3.2.0. No telemetry. Declares ONLY runFullTrust.

What changed (feature level):
- The widget now renders per-model weekly limit meters that Anthropic's
  claude.ai usage API recently began publishing (e.g. the Claude Fable 5
  weekly allowance included in subscription plans as of July 20, 2026). Same
  usage endpoint the approved versions already read; the app simply displays
  additional entries from the same response.
- Sign-in guidance: users whose Claude account was created with Google
  sign-in (which Google does not permit inside embedded browser windows —
  documented in our 3.1.0 letter) are now walked toward claude.ai's own
  "Continue with email" login, where Anthropic emails them a one-time code
  they enter on the claude.ai page inside the app. This is claude.ai's
  first-party login flow rendered unmodified; the app still only reads back
  its own session cookie afterward, exactly as reviewed. The manual
  session-key paste remains available as a fallback.
- Alert tuning: per-model limit alerts are now visual-only by default with a
  Settings checkbox to enable their chime; aggregate limit alerts unchanged.
- Reliability: an automatic one-time retry after switching accounts.

Preserved: tool-strip navigation with accessible names, themed dialogs
legible in light/dark, vault consent/erase surfaces, runFullTrust for local
AppData + the DWM Mica API only. No data leaves the device.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated with,
endorsed by, or associated with Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v3.3.0.0)

- [ ] Version trio in csproj + `<Identity Version>` = `3.3.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.3.0.0.msix` built off the `v3.3.0.0` tag, unsigned
- [ ] ONLY `runFullTrust` declared; trademark disclaimer on all six surfaces
- [ ] Fable meter renders from live data; "What's new" filled from `listing-copy-3.3.0.md`
      (public field — separate from these notes; BOTH filled every release)
- [ ] Store description's WHAT YOU NEED Google paragraph updated to the email-code path
      (delta in `listing-copy-3.3.0.md`)
- [ ] `dotnet test` green (474)

## Source

Supersedes [`reviewer-letter-3.2.0.0.md`](./reviewer-letter-3.2.0.0.md). Public listing copy:
[`listing-copy-3.3.0.md`](./listing-copy-3.3.0.md).
