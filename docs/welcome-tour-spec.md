# Welcome tour: shared spec (Mac and Windows)

A first-run experience built on the What's New cards, so a new user meets Sanduhr the way an updating user meets a release: a few confident cards, each showing the real thing, each with a way to go see it. Replaces backlog #3's walkthrough sketch. Both apps build to this spec; platform notes are marked **Mac** and **Windows**.

## Why this shape

The What's New window (Mac 2.6.0) works because each card is a feature in one sentence plus a live preview, and Show me puts you on the real page. A first-run tour has the same job at a larger scale, so it should use the same parts: the card table, the preview views, the Show me routing. Today a new install gets a sign-in step and then a bare widget; nothing points at the Desk, the notch or tray, accounts, the Usage page or the integrations.

## The flow

1. **Sign in** (step 0, not a card). Embedded sign-in: Windows today, Mac with port item M1 (`docs/windows-port-plan.md`). Paste is the fallback. The tour waits until a first fetch succeeds, so its first card can show the user's own numbers.
2. **The tour window**: the What's New window's layout and look, with a step header instead of the release header ("Welcome to Sanduhr · 1 of 5"), one or two cards per step, Back / Next, and **Skip the tour** always visible.
3. **Steps** (each a card or a pair; data, not code):

| Step | Card | Live preview | Choice made here (a real setting) | Show me |
|---|---|---|---|---|
| 1 | **Your limits, paced**: the meters and the pace marker, the burn-rate projection | the user's own meters, live | none | the widget |
| 2 | **On your desktop** (the Desk; hidden on Windows until W9 ships) | a Desk corner with the user's meters and today's date | Desk on or off; Match Desk theme or keep the widget theme | the Desk / Settings, Desk |
| 3 | **At a glance** (Mac: notch and menu bar; Windows: tray) | Mac: the notch wings; Windows: the tray glyph | what the menu bar or tray shows (session, weekly, higher, both in turn) | Settings, General |
| 4 | **More than one account** | the account chip | none ("Add an account" opens Accounts) | Settings, Accounts |
| 5 | **Claude Code, connected** (the Usage page, the MCP tools, the statusline, the glow) | the Usage page's overview, or the statusline line | none; each integration installs from its page, with its own consent | Settings, Integrations |

4. **Done**: a last line ("You can take the tour again from About or the Help menu") and **Finish**. The widget stays where it is.

## Rules

- **Shown once**: after the first successful sign-in on a fresh install. Never on update (What's New covers updates), never during sign-in, never twice. A defaults/settings flag records finished or skipped.
- **Skippable at every step**; skipping leaves every setting as it was. Choices made before skipping stay.
- **Choices are real settings**, written when chosen, so Back shows them as made. Nothing installs from the tour itself: integrations go through their own pages and consent dialogs.
- **Reopen any time**: About ("Take the Tour…") and the Help or app menu. Reopening shows the same steps with the current settings.
- **A step hides itself when its feature is missing** (Windows before the Desk ships; a Mac without a notch shows the menu bar card alone).
- **No network beyond what the app already does.** Previews draw from data already fetched.
- **Accessibility**: keyboard through every step (Tab, Return for Next, Escape for Skip), VoiceOver / Narrator labels on every preview, Reduce Motion / animation settings respected (previews still, no write-in).
- **Copy rules**: Sanduhr's voice doc; the trademark disclaimer on the window (10.1.4.4 (a) applies to Windows Store builds).

## Shared data model

One table per app, same shape as What's New's cards, with a step field:

- `id`, `step`, `title`, `body` (two sentences at most, 200 characters), `preview` (a named preview view), `symbol` (fallback icon), `showMe` (a destination), `choice` (an optional setting control), `platforms` (`mac`, `windows`), `requires` (a feature flag that hides the card when missing).
- What's New cards and tour cards share the preview views and the Show me router. A card can appear in both tables.
- Later features add a tour card the same way they add a What's New card.

**Mac**: extend `Models/WhatsNew.swift` with the tour table and `Views/WhatsNewWindow.swift` with the step header and choice controls. **Windows**: build W1 (What's New) and the tour together, porting the Mac's card model and window.

## Verify

- Unit tests: which steps show for a platform and feature set; the shown-once flag (fresh install, update, skip, finish, reopen); choices written and read back; Show me destinations.
- Smoke: a fresh-profile scenario (Mac smoke, Windows smoke plan) that signs in with a fake key, sees the tour, steps through, skips, and checks the flag and the settings.
- By hand: a fresh user account on each OS.

## Open questions

- Does the tour offer the MCP integrations directly, or only point at them? Proposed: point at them; installing writes into Claude Code's config and deserves its own page and consent.
- Should numbers show as a demo until the first fetch, so the tour can open right after sign-in without waiting? Proposed: wait for the fetch (usually a second or two); show a placeholder card if it fails.
- Windows before the Desk ships: step 2 shows the pinned widget and Match Desk, or is hidden? Proposed: hidden until W9, so the tour never promises a Desk that isn't there.
