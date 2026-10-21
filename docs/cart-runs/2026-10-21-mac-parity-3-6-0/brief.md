# Run Brief: mac-parity-3-6-0

## Ask
"Yeah, let's go ahead and take this one." (Este, 2026-10-21 13:44, on the 3.6.0 Mac-parity slice.) Scope set the same day: "1 add it" (Desk under desktop icons, behind a spike) and, on the statusline label, "if the mod is labeling them and not Sanduhr, then I think we're okay."

## Source
- **Kind:** pasted list (parity inventory, 2026-10-21, verified against `windows-dotnet/src`)
- **Location:** dashboard decisions `jKcxlY6KNcch787LzH5y` (scope) and `NAXBbyspkoqoEDXI8bv6` (under-icons mechanism), project `FX7zznEFlzArQ3CTY7RU`

## Intent
Windows catches up with the Mac features that ship cheaply and safely: the tour knows about the Desk, the Desk gets its plate, arrange and quit match the Mac, the statusline says which folder it's in, Claude can watch work and suggest Desk messages, and (if the spike holds) the Desk can sit under the desktop icons.

## Done when
- Every go item is built, tested (`dotnet test windows-dotnet` green) and committed on `feat/mac-parity`.
- The spike has a written verdict in `windows-dotnet/docs/spec.md`, and item 6 is built or dropped on that verdict.
- The MCP tool change (item 7) is built and checked, and Este has seen the widened trust-boundary allowlist before it is committed.

## Out of scope
- Meetings on the Desk (needs the Appointments capability; its own cycle).
- Mod authoring and sharing (MCP write tools that run `git`/`claude`; its own cycle).
- Version bump, release notes, Store copy and the release itself (3.6.0 release is a separate step).
- Mac-only surfaces: notch, island pill, menu-bar hourglass, Sparkle, Homebrew, Keychain.

## Inferred
- Order puts the spike first because it is the only item that can fail and it decides item 6 (from: Este approved "spike first" ordering, 2026-10-21 07:00).
- The statusline label comes from the Claude Code folder at render time (CLAUDE_CONFIG_DIR basename; plain `.claude` shows none), overridable by `env.SANDUHR_LABEL` in that folder's `settings.json`; Sanduhr never writes an account name to disk (from: Este's "mod labels them, not Sanduhr" + decision jKcxlY6KNcch787LzH5y).
- Under-icons copies the Mac 2.27 shape: visuals behind the icons, the existing V3 window as a transparent click layer for clickable pieces, menus only with the option off, off by default (from: CHANGELOG v2.27.0-mac).
- Item 7 is class hold: it widens the MCP trust boundary (permissions) (from: take stop-class defaults + CLAUDE.md "Widening the link list is a design decision that must break CI").
- No PR is opened during the run; it is offered at the close (from: take § 7 push step).

## Mode
- **Mode:** Attended (autonomous engine, profile `fully-autonomous`, persona architect)
- **Triggered by:** attended by default (13:44, 7 items, no overnight ask). Stops: the spike verdict (item 1) and before committing the widened allowlist (item 7).
