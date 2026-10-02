# Pending 626Labs Dashboard decision-log entries

> **What this file is:** decision-log entries that belong in the 626Labs Dashboard
> (`manage_decisions log`) but were written in a session where the dashboard MCP was not loaded.
>
> **What to do with it:** log each entry (one call per `---`), then `git rm` this file. Entries
> 1 and 2 belong to the Sanduhr project, entries 3 and 4 to rororo-mac.

---

## Entry 1 — Mac signing material recovered after a reinstall; 1Password becomes the master copy

**Type:** Overcame a momentous hurdle + Architectural choice
**Project:** Sanduhr für Claude (Windows) — dashboard project for this repo
**Date:** 2026-10-01 / 2026-10-02

### Summary

`docs/mac-release.md` assumed the Mac that signed 2.0.4 still held the Developer ID certificate
and the Sparkle EdDSA key in its login keychain. That Mac had been reinstalled: zero signing
identities, no `sanduhr-notary` profile, no Sparkle key. The Sparkle key cannot be replaced: every
2.0.4 install only accepts updates signed with the key behind
`SUPublicEDKey` `b5hOsbZVS61d3/wAN8hxf70HvfH3SOpuNVZES3QvhpI=`. Losing it meant 2.0.4 users would
need a manual download for every future version.

macOS had kept the unopenable old login keychains as
`~/Library/Keychains/login_renamed_1.keychain-db` and `login_renamed_2.keychain-db`. Unlocked with
the pre-reset password, `login_renamed_2` held the Sparkle key (`generate_keys -p` printed the
expected public key after `-f` import) and the Developer ID Application and Installer identities
(certificate valid to 2031-09-14).

Decision: signing material lives in 1Password (master) and the GitHub `mac-release` environment
(what CI reads). No Mac's Keychain is authoritative. The environment requires reviewer approval and
deploys from `main` only; Actions may open the appcast PR.

### Gotchas worth keeping

- After a reinstall or password reset, check `~/Library/Keychains/login_renamed_*.keychain-db`
  before declaring signing keys lost.
- RORORO and Sanduhr share one Sparkle key (Sparkle keeps one per Mac user account), so one
  1Password item backs both apps.
- Claude Code's auto-mode classifier refuses to read private keys from the Keychain. Secret
  handling has to be run by hand, one command per prompt: a pasted multi-command block fed
  `gh secret set` prompts with the wrong lines and overwrote three rororo secrets (since fixed).

### Evidence

- `docs/mac-release.md`: 1Password table and "After a reinstall" section (this PR).
- `mac/scripts/export-release-secrets.sh` dry run then upload: all five `mac-release` secrets set
  2026-10-02 04:47 UTC.

---

## Entry 2 — Sanduhr for Mac 2.1.0 shipped through the CI release workflow, verified end to end

**Type:** Milestone + Gotcha discovered
**Project:** Sanduhr für Claude (Windows)
**Date:** 2026-10-02

### Summary

The first CI-built Mac release (`.github/workflows/mac-release.yml`, run 36999175541) published
`v2.1.0-mac`. Verified from the downloaded `Sanduhr-2.1.0.dmg`:

- Gatekeeper accepts the DMG and the app: "Notarized Developer ID", team 82BSR56X5J; ticket stapled.
- Universal binary: `x86_64 arm64`.
- `CFBundleShortVersionString` 2.1.0, `CFBundleVersion` 3, `SUPublicEDKey` unchanged.
- The live appcast's `sparkle:edSignature` for 2.1.0 verifies against the original public key, and
  `length` (3,198,809) matches the DMG. 2.0.4 clients will accept the update.

### Gotcha

Run 36970519689 failed at DMG creation with `hdiutil: create failed - Resource busy`, a transient
GitHub macOS-runner failure; the rerun passed. Worth a retry loop around `hdiutil create`.

---

## Entry 3 — rororo-mac release secrets re-set from 1Password; expired tap PAT found

**Type:** Gotcha discovered + Scope change
**Project:** rororo-mac (repo `github.com/estevanhernandez-stack-ed/rororo-mac`)
**Date:** 2026-10-02

### Summary

- v0.8.0 (2026-09-13) signed, notarized and published, but its "Bump Homebrew tap" step failed:
  `HOMEBREW_TAP_PAT` had expired. The cask was bumped by hand.
- `MACOS_CERTIFICATE` + `MACOS_CERTIFICATE_PASSWORD` were re-set as a pair from the 1Password
  Developer ID p12. `HOMEBREW_TAP_PAT` (new fine-grained token, `homebrew-rororo` only, Contents
  read/write) and `APPLE_NOTARY_PASSWORD` were re-set too.
- Verified without releasing: a temporary workflow on a throwaway branch imported both p12s,
  matched both identities to their `*_NAME` secrets, logged in to notarytool, matched
  `SPARKLE_ED_PRIVATE_KEY` to `SUPublicEDKey`, and confirmed the tap PAT has push. All passed
  (run 37047218110); the branch was deleted.

### Follow-ups

- The new tap PAT was created without an expiration date, so the v0.8.0 failure mode should not recur.
- rororo-mac PR #14 documents where each secret lives and the reinstall recovery, and corrects the
  stale README claim that both p12 passphrases must match (true only before `e4fee0a`).

---

## Entry 4 — Release secrets: one master (1Password), one reader (GitHub), per-app tables in docs

**Type:** Architectural choice (cross-project)
**Projects:** Sanduhr für Claude (Windows), rororo-mac
**Date:** 2026-10-02

### Summary

Both Mac apps now follow the same rule: 1Password is the only master copy of signing material,
GitHub Secrets is what CI reads, and each repo's release doc maps every 1Password item to the
secrets it backs. Rotating a secret means updating both in the same sitting. Rationale: the login
keychain was reset twice this year (July and September, per the two `login_renamed_*` files), and
only the renamed keychain saved the irreplaceable Sparkle key.
