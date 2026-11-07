# Run Brief: widget-from-file-2-42

## Ask
Build Mac 2.42.0 from docs/mac-widgets-spec.md section 20, "Install a widget pack from a file" (issue #408, Mac half). Spec approved by the owner 2026-11-07. Work on a cart/widget-from-file-2-42 branch off main; the uncommitted spec edit goes in as the branch's first commit. Autonomous mode.

## Source
- **Kind:** spec section
- **Location:** `docs/mac-widgets-spec.md` section 20 (Release 2.42.0); issue #408; the Windows half in PRs #437 and #440 (`windows-dotnet/`, read only)

## Intent
A pack made outside Sanduhr (a SpriteForge export, the logo script, a hand-packed zip) that passes `validate.py --strict` installs on the Mac from Settings ▸ Widgets, lists as made here, and, when it is a sprite, walks on the Desk as the companion. The release is a complete 2.42.0: card, changelog, privacy, smoke section, version lockstep.

## Done when
- Settings ▸ Widgets has **Install widget from file…** beside **New sprite from an image…**; a good zip installs as `local` and a sprite becomes the companion with the spec's sentence for the Desk's state.
- A bad zip (missing LICENSE, a blocked word, an oversize sheet, a link, a catalog id clash, a `sanduhr_min` too new) installs nothing and the status line says why.
- `WidgetFileInstallTests` covers the seven cases in section 20, and `./test.sh` passes on the integration branch.
- `mac/Info.plist` is 2.42.0 build 57, the What's New card `widget-from-file` is in the table with its `WhatsNewTests` lines, CHANGELOG has the entry under `## Unreleased (mac)`, PRIVACY.md and `mac/README.md` say what is true, smoke section 75 exists with a passing scenario.

## Out of scope
- Drag and drop onto the Widgets page (spec: nice to have, not 2.42.0).
- Anything in `windows-dotnet/` or the catalog repos.
- Pushing, the release workflow, the appcast, the cask (the push step and `docs/mac-release.md`, after the owner's live walk).
- A streamer-mode name mask (the Mac has no streamer mode).

## Inferred
- The run is unattended in layout (integration branch, item worktrees, local merges, nothing pushed) because the ask names a `cart/` branch and autonomous mode, and the owner is leaving for the day. (from: the ask; the 2.41.0 run `docs/cart-runs/2026-11-05-setup-2-41` used the same layout)
- The spec commit is on local `main` (`906f7feb`), one ahead of `origin/main`, so the helper's base includes it; the integration branch therefore carries it as its first commit and `main` fast-forwards after the PR merges. (from: the ask's "first commit" wording and the helper cutting from HEAD)
- Items are split so each has a local check that runs in its worktree (`cd mac && ./test.sh`, `ruby smoke/selftest.rb`, `python3 -m pytest -q integrations/tests`). (from: the 2.41.0 list's shape)
- Workers dispatch at the `bulk` tier; collect-and-verify stays on the session. (from: the global tier map)
- Every item is `go`: no money, auth, permissions, secrets, prod data or deploys are touched. The one file picker reads a user-chosen zip, which the spec and PRIVACY cover. (from: `unattended-run.md` § Stop classes)

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so (a named `cart/` branch, autonomous, the owner away for the day); permission mode confirmed bypass by the session's prompt-free git and gh calls
