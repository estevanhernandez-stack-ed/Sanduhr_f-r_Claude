# Run Report: setup-4-5

13 merged, 0 held, 0 parked, 0 stopped.

Set up Sanduhr is built: one screen of free choices (a nicer desktop, tidy my files, track my AI usage) on first run and from the tray and Settings, Just the Desk retired with a migration, usage you can turn off without signing out, desktop off leaving only drawers, and Design it with an AI chat. Every item had a fresh implementer and a fresh reviewer; the branch had a whole-branch review that found three Important seams, all fixed in item 13 and re-reviewed READY.

## Ready to push
- **Integration branch:** `cart/setup-4-5` (base `52a97067`, the spec and plan commit on `docs/setup-spec`). 35 commits, 63 files, +4947 / -473.
- **Items:** 1 setup model and migration, 2 setup in settings.json, 3 setup copy, 4 usage off and launch (Just the Desk removed), 5 desktop off draws only drawers, 6 setup service, 7 Set up Sanduhr window and first run, 8 tray and Settings entries and the 4.5.0 card, 9 AI chat prompt and paste reader, 10 Design it with an AI chat in the app, 11 harness shots, 12 full suite, 13 whole-branch review fixes.
- **Checks:** on the integration tip `4a3a04e5`: `dotnet build windows-dotnet/Sanduhr.slnx -c Release` 0 errors; `dotnet test windows-dotnet/Sanduhr.slnx -c Release`: Core 4960/4960, App 1323/1323, Mcp 300/300 (TrustBoundaryTests green; the MCP project is untouched). Harness shots of the setup and chat windows in obsidian and aurora were checked by eye (scratchpad, untracked).
- **Close-out gates:** vibe-test and vibe-sec gates were not run; the repo's own full Release suites stand in (see D19).

To push and open the PR:
```bash
git push -u origin cart/setup-4-5
gh pr create --base main --head cart/setup-4-5 --title "feat(setup): Set up Sanduhr, one screen of free choices and a way back"
```
Pushing waits for your say-so.

## Waiting on you
None held. Before a 4.5.0 release, three things need you:
- **Live check (plan Task 12, step 2), on a dev profile:**
  1. Fresh first run with the default ticks: the drawers review on the Desk, Apply, then the tour (no dialog in between).
  2. Set up Sanduhr from the tray: tick usage, sign in, untick usage. The widget shows "Usage is off · Set up Sanduhr", and the account stays.
  3. Design it with an AI chat: Copy prompt into a real chat, paste its answer, Preview, Apply, Put my Desk back; then Use these drawer rules.
  4. Copy a 4.0.1 profile with an account (Desk off) and one with Just the Desk; launch each: nothing changes, and turning the Desk on later from Settings draws the clock.
- **Two tour calls** (left by the whole-branch review): the welcome tour is keyed on usage only, so a drawers-only user gets no drawers card and a tray card naming Arrange Desk; and a first run put away with Not now and then signed in may never get the tour. Fine for 4.5.0, or add a drawers card.
- **Release steps:** the version is still 4.0.1.0; the 4.5.0 What's New card shows only after the csproj trio and the manifest are bumped (docs/release-runbook.md). The reviewer letter needs the setup and AI chat paragraphs.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md` (D1 to D19). Highest stakes:
- D7: usage off stops every Claude fetch (seat meters and following too), clears the numbers and deletes the usage snapshot; a sign-in with no setup yet shows the widget without saving a setup.
- D9: a drawers review never brings the hidden desktop back; a design review does.
- D15/D16: pasted designs are limited to the arrangement fields the prompt teaches; a chat's drawer rules are checked before anything is written and only match drawers Sanduhr made, never a view of a real folder.
- D18: the whole-branch fixes (upgraders keep the desktop on, the widget's usage-off row, the trademark line whenever the screen names Claude).
- D11: a default first run ends with no notice dialog; the tour waits for the drawers result bar.

## Left untouched
Uncommitted files in your tree at run start (still there, not touched):
- `.playwright-mcp/`
- `dist/`

The run folder `docs/cart-runs/2026-10-31-setup-4-5/` itself is uncommitted in your tree on `docs/setup-spec`.
