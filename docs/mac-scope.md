# Sanduhr für Claude, the Mac app: scope

- **Date:** 2026-10-18, written against 2.27.0 as it ships.
- **Type:** Scope of record for a shipped app, written after the fact so the next scan has a user to aim at.
- **Lineage:** `docs/scope.md` is the June 2026 Windows rebuild's scope and stays the Windows document. The Mac app's per-item specs are indexed in `docs/spec.md` under "The Mac app"; `mac/README.md` is its behavior reference.

## Idea

A Mac menu bar app that turns a claude.ai subscription's limits into pacing you can read without opening a window: on the desktop itself (the Desk), around the camera notch (the island), in a small floating widget, beside the hourglass in the menu bar, and inside Claude Code through a statusline, an MCP server and a meters mod. The Desk earns its place with no Claude plan at all.

## User

A Claude subscriber on a Mac who wants to know how fast the session and weekly limits are burning without switching to a browser or a window. Three of them in particular:

- **The one with a full desktop.** Files cover the screen. The Desk's text has to stay readable over them (the readability plate), and the files have to stay the Finder's: movable, clickable, droppable (the click layer under the icons, 2.27.0).
- **The one with no notch.** A Mac mini, an iMac, an external display or a clamshell MacBook. The island, its wings and the camera strip need the notch today; the glow and the camera light fall back to a spot at the top center of the main screen.
- **The one with no Claude plan.** Picks **Just the Desk** on the welcome sheet (`docs/BACKLOG.md` #4, `mac/Sources/Sanduhr/Models/JustTheDesk.swift`): the Desk on, the widget following its own setting instead of waiting for sign-in, the sign-in reminder off, every Claude piece back the moment a key is saved, and a Desk-only tour.

## Problem

claude.ai shows the limits as numbers on a page. Pacing is a feeling you need during the work: ahead of the reset or behind it, and whether the local Claude Code session is burning tokens faster than the lagging usage endpoint admits. The Mac app puts that feeling where the eyes already are and stays out of the way of everything else on the screen.

## In scope: the surfaces, and what each is for

| Surface | Where it lives | What it is for |
|---|---|---|
| The Desk | `mac/Sources/Sanduhr/Desk/`; "Desk layout" and "Desk clicks" in `mac/README.md` | The desktop layer: the Claude meters, the clock, the day's message, meetings, now playing, watchers and the claude line, placed in eight places and sized per piece. Under the icons when asked, with the click layer (`mac/Sources/Sanduhr/Desk/DeskClickLayer.swift`) serving the small pieces a click acts on. |
| The island and its wings | `mac/Sources/Sanduhr/Desk/NotchView.swift`, `mac/Sources/Sanduhr/Desk/NotchGlow.swift`; "Camera and mic indicators" in `mac/README.md` | The black island around the camera notch: a wing of text either side (the Claude meters, now playing or the next meeting; an empty wing collapses), a click to Settings, a two-finger menu, the glow for Claude Code's waiting and done, and the camera and mic lights beside the camera. |
| The widget | `mac/Sources/Sanduhr/Views/` | The floating window: the limits as bars with reset countdowns and pace, the accounts, a row of buttons. The one surface a new install opens with after the welcome sheet. |
| The menu bar | The hourglass item and the shared Sanduhr menu (`mac/Sources/Sanduhr/Models/SanduhrMenu.swift`) | A percent beside the hourglass; a click shows or hides the widget; the menu reaches Settings, Arrange Desk and the rest from anywhere. |
| The Claude Code integrations | `mac/integrations/sanduhr_mcp.py`, `mac/integrations/sanduhr_statusline.py`, `mac/integrations/mods/sanduhr-meters/` | The statusline segment and the meters band above the prompt read `snapshot.json`; the `sanduhr` MCP server answers what each account chose to share and hands Claude's requests (Desk messages, themes, now-playing looks, watchers, mods, configs) back to the app through files. |
| Settings and the smoke tools | `mac/Sources/Sanduhr/Views/`, `mac/smoke/` | Settings v2: pages by task with live previews; the debug hooks, the state snapshot and the scenarios (`mac/smoke/scenarios/`) that let a run see what the app shows without a hand on the mouse. |

## Constraints that hold every item

- **Made-up data in screenshots.** The screenshot set (`docs/screenshots/mac/`) comes from demo mode (`mac/Sources/Sanduhr/Models/DemoMode.swift`, `mac/smoke/scenarios/demo-every-surface.yaml`) on a clear desktop: sample accounts and sample numbers, never a real account, name or meeting.
- **No network from the MCP server.** `mac/integrations/sanduhr_mcp.py` is Python standard library, reads only the files the access file names, and makes no request; the statusline and the mod are the same. `docs/PRIVACY.md` is the contract, per surface.
- **The trademark disclaimer wherever Claude is named.** Sanduhr is an independent tool, not made by or affiliated with Anthropic. The line goes on every surface and every piece of copy that names Claude: the Windows Store's 10.1.4.4 bar in `CLAUDE.md`, carried to Mac copy by `docs/BACKLOG.md` #4 and `docs/setup-guide.md`.
- **Sparkle plus the Homebrew cask.** Updates reach users through the Sparkle feed in `docs/appcast.xml` and the cask in `docs/distribution/sanduhr.rb`, built signed and notarized the way `docs/mac-release.md` says; the Sparkle key cannot be replaced, and the build number only goes up.
- **The smoke harness is the acceptance surface.** An item is done when `mac/build.sh --debug`, `mac/test.sh` and `mac/smoke/selftest.rb` pass, the fixture `mac/smoke/fixtures/snapshot/state.yaml` knows its keys, and the by-hand twin is in `docs/mac-smoke-test.md`. Anything on the Desk is also looked at on a real desktop before its design is approved.
- **Existing users keep their choices.** A new default is written on first run and read as the old behavior when its key is absent, so no Desk changes its look without the user's say.

## Explicit cuts

- **No Windows parity inside Mac items.** `windows-dotnet/` is the Windows agent's. A Mac item that wants parity files its own Windows item and stops there.
- **No account features beyond claude.ai's own.** An account in Sanduhr is a claude.ai account: a label, a session key in the Keychain, and what the subscription reports. No Sanduhr sign-in, sync, sharing or billing of its own.
- **No icon-aware Desk without Accessibility.** The click layer serves only the small pieces a click acts on; the big pieces stay under the files. Yielding to a file under a piece needs Finder's icon positions, and reading them needs the Accessibility permission. "Desk under the icons" in `docs/spec.md` records the probe and the route.
- **No Touch Bar island** until a Touch Bar Mac and a probe of its private APIs say it can be done (issue #119, Proposal B).

## Success criteria

1. Each of the three users above gets a first run that works with nothing beyond the welcome sheet: readable text over files, the right thing at the top of the screen with or without a notch, and a Desk with no account.
2. Every release passes the smoke harness and the by-hand smoke test, carries the disclaimer, and ships through both Sparkle and the cask.
3. No surface sends anything anywhere the user did not switch on, and `docs/PRIVACY.md` says what each surface reads and writes.
