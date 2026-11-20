# Windows 5.0: the growth release

**Status:** shape approved by Este, 2026-11-17. Each part below gets its own spec and plan.
**Ships as:** 5.0.0.0, one Store submission and one GitHub release. 4.9.0.0 was the last 4.x; a 4.9.x patch stays possible.

## Goal

More users, from two groups in one release:

- **Any-agent users.** People who pace Codex, the Antigravity CLI, Claude Code or a mix, not only Claude subscribers.
- **Desktop customizers.** People who come for the Desk and may never sign in to an AI plan.

Each group gets a lead feature, and the trailer and Store listing carry both pitches.

## The five parts

| Part | Spec | What it is | Lead for |
|---|---|---|---|
| Agent notifications | spec 4 | Working, finished and waiting signals per agent, on the glow, the taskbar flash, a Desk card and a Windows toast | Any-agent users |
| Ready-made Desk looks | spec 5 | A gallery of complete designs applied in one click, plus a neon glow on the ink | Customizers |
| Cleanup and organization | spec 6 | Settings pages and names reorganized for a multi-agent app, and the three 4.9 smoke minors | Both |
| Arm64 | spec 7 | A second Store package, an arm64 GitHub installer with its own update feed, and a one-time notice for x64 installs on Arm PCs | Both |
| Trailer and listing | no spec, a release task | New trailer, new screenshots, a description that carries both pitches | Both |

## Decisions already made

- **Signals ship as they are readable, labelled per agent.** Finished and working for Claude Code, Codex and the Antigravity CLI. Waiting-on-you for Claude Code only: neither Codex nor the Antigravity CLI writes that state to disk on the machine checked (event names only, 2026-11-17). Limit alerts stay as they are. Settings says which agent supports which signal.
- **Four surfaces, each with its own switch.** The glow, the taskbar flash and the Desk watcher card extend from Claude Code to every agent. The Windows toast is new.
- **Codex's finished signal is exact.** Its rollouts mark every turn with `task_started` and `task_complete`. The Antigravity CLI's is inferred from the last step's status plus quiet time, so it is conservative.
- **A notification needs a live watcher.** The Vault's five-minute ingest is too slow.
- **Ready-made looks reuse the design format `propose_desk` writes,** and Put my Desk back is the undo.
- **The spectrum visualizer is a stretch.** It captures sound, so it would be opt-in and would change the privacy page and the reviewer letter. The neon glow has no privacy cost and is in.

## Order

Spec 4, spec 5, spec 6, then the trailer and listing once the features exist to film. Arm64 runs alongside from early on, because it touches only the build and release pipeline and is the part most likely to surprise.

## If scope threatens the release

Cut in this order: the spectrum visualizer, then arm64 to 5.1, then the neon glow.

## Out of 5.0

- A waiting signal for Codex (task filed, after 5.0).
- More sources (Cursor, the Gemini CLI, local models) and the Gemini meter.
- Rainmeter import.

## For the release paperwork

Carried here so they are not lost when the 5.0.0.0 reviewer letter, Store copy and hosted privacy text are
written (release-runbook Phases 2 and 7).

- **Reviewer letter: a trademark line for toasts and Desk cards (owner's decision, 2026-11-18).** A
  notification's title and a Desk card's title name Codex and Antigravity ("Codex finished",
  "Antigravity · project") where no notice can sit beside them. The letter states that these names are used
  nominatively to say which agent's turn ended, with the usual statement that "Codex" is a trademark of
  OpenAI and "Antigravity" is a trademark of Google LLC and that Sanduhr is not affiliated with either. The
  Notifications page itself carries all three notices.
- **Reviewer letter: the disclosure change from spec 4.** No new destination, capability or library. The
  Claude Code hook now writes one small file per event under `%USERPROFILE%\.sanduhr` naming the event, the
  folder and the session, which Sanduhr deletes as it reads; a Windows notification shows the agent, the
  project's name and how long the turn ran.
- **Hosted privacy page.** `docs/store/hub-privacy-5.0.0.md` holds spec 4's paragraph; each later 5.0 part
  adds its own. File the hub issue and check the page before the Store commit.
- **Arm64 (spec 7).** Reviewer letter: one sentence that the submission carries an x64 and an arm64
  package with the same features and the same capabilities. Store Listing Console:
  `apps/sanduhr/config.toml` lists both package files under `packages`. Partner Center: both packages
  go into the Packages page of the one submission. What's New: a line that Sanduhr is now native on
  Arm PCs. The Windows download links (`INSTALL.md`, `docs/index.html`) move from `releases?q=v4` to
  `releases?q=v5` before the tag; the release workflow refuses the tag until they do. Run the Arm
  check on the draft before publishing (release-runbook Phase 6). In that same change, add the small
  print "Arm PC? Take the win-arm64 installer on the same page." under the Windows download button in
  docs/index.html.

## Open

- The three 4.9 smoke minors for spec 6: the archive list shows a newly switched-on agent only after Settings reopens; the Sources header names only Codex; the stop-archiving dialog's buttons are not exposed to UI Automation.
- The restart-to-update check on a real Store install, which decides whether the 0x80073D02 known issue leaves the 5.0 notes.
