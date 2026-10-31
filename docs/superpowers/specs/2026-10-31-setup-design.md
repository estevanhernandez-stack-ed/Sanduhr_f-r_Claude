# Set up Sanduhr: design

Windows. Brainstormed with the owner on 2026-10-31, after drawers plans 1 and 2 merged (PR 399). Dashboard
decisions: `fJL5UpvpO74C4oZYB1Op` (5.0 reframed: setup for anyone, drawers is incremental),
`5rvfW1084ELd3jp9qzRK` (Just the Desk is a one-way door), `ZwLGJzauXUWiAxddKRp8` (one screen of free choices,
not doors), `p1IMggjkmhlBbSAHI8Ne` (Design it with an AI chat).

This spec replaces the three front doors of the drawers spec (`2026-10-28-drawers-design.md`, section 3, and
plan 3's first item) and the drawers What's New card.

## Why

A first-time user, set up by someone else, wanted a nicer desktop and tidy files. The person setting it up
chose the Claude path by mistake, and there was no way back. Today, in 4.0.1:

- The first-run screen is a Claude sign-in card ("Track your Claude usage"), with Just the Desk as a second
  button.
- Just the Desk shows only on that card, only while no account exists.
- Signing in ends Just the Desk (`JustTheDesk.EndBySignIn`).
- Settings, Widget can only end Just the Desk, never turn it on.
- The only way back is removing every account, which brings the first-run card back.

Three exclusive doors (Just drawers / Desk / Desk and usage meters) would not have helped: this user wanted
two of them. Doors also multiply with every agent source 5.0 adds.

## The goal

Sanduhr asks what you want before it mentions any account. The choices combine freely, and every choice can be
changed later in one place, signed in or not.

**Success:**

- Someone with no AI tool installs Sanduhr, keeps the two default ticks, and accepts the first drawers plan.
  They have a nicer, tidy desktop in under two minutes and never see a sign-in screen.
- Anyone who picked wrong reopens Set up Sanduhr from the tray and changes it in one step.

## Out of scope

- Agent sources other than Claude (Codex is spec 2 of 5.0; Gemini and local models are spec 3).
- The rename to Sanduhr and Sanduhr Desk, and the Store listing (spec 4).
- Drawers styling and placement rules: their own spec, next. See section 6 for where the two meet.
- Plan 3's other items: `propose_drawers` / `get_drawers` with the Privacy switch, auto-tidy, drag-to-reorder
  rules, the harness drawers scenario.
- The Mac.

## 1. The setup model

Setup is three choices. Each is stored on its own in `settings.json`:

| On screen | What it controls | Stored as |
|---|---|---|
| **A nicer desktop** | The Desk's pieces other than drawers: the clock, message, weather and the rest | new `setup.desktop` (bool) |
| **Tidy my files** | Drawers | the existing `drawers` on/off, unchanged |
| **Track my AI usage** | The meters, per source | new `setup.usage`: an object of sources, each on or off. Only `claude` in this spec; spec 2 adds `codex`. |

**The Desk runs whenever desktop or drawers is ticked.** Setup writes `desk_enabled` as `desktop || drawers`.
Drawers alone gives a Desk with only drawers on it (the drawers spec's Just drawers, now as a combination).
With desktop off, the Desk view leaves out every non-drawer piece and keeps the layout, so ticking desktop again
puts each piece back where it was.

**Usage off:**

- The widget stays hidden and fetching stops; the tray stays.
- Saved accounts are kept, never deleted. Ticking usage again resumes with the saved account and no new sign-in.

**Usage on with no account:** the widget shows today's sign-in card, unchanged.

**Signing in changes only usage.** It never switches the Desk or drawers. `JustTheDesk` stops being a mode:
"the widget starts hidden" follows from usage being off.

**At least one choice stays ticked.** With all three unticked, Continue is disabled and one line says why
("Pick at least one, or Sanduhr has nothing to show."). A launch never shows nothing.

**The Desk page's own switch stays.** Turning the Desk off there hides the Desk, drawers included, without
returning any file, as today. The setup screen shows desktop as ticked only when `setup.desktop` and
`desk_enabled` are both on.

### Migration

Runs once, on the first launch of this build, before anything draws. It writes `setup` only when the key is
absent:

| Found | Becomes |
|---|---|
| An account (any) | usage `claude` on; desktop = `desk_enabled`; drawers unchanged |
| `just_the_desk` on, no account | usage off; desktop on; Desk on; drawers unchanged |
| Both | as an account (an account wins, as sign-in did) |
| Neither | no `setup` key: this is a first run, and the setup screen opens |

The `just_the_desk` keys stay in the file, unread from this build on, so a 4.x build reading the same settings
still behaves. The sign-in reminder keeps its own switch. Migration turns it back on only where Just the Desk
had silenced it (`just_the_desk_silenced_reminder`).

## 2. The setup screen

**Where:** its own small window, centred, opened before anything else on a first run. It replaces the
widget's first-run sign-in card (`SignInReason.FirstRun` no longer shows "Track your Claude usage" on a fresh
install). It follows the app theme, works in light mode, and every control has an accessible name (10.1.4.4).

**What it shows:**

- Title **Set up Sanduhr**, and one line: "Pick what you want. You can change this any time from the tray or
  Settings."
- Three tick tiles, each a title and one sentence:
  - **A nicer desktop.** "A clock, a message and the weather on your desktop." Ticked on a first run. Under it,
    the link **Design it with an AI chat** (section 3).
  - **Tidy my files.** "Sort your desktop files into drawers you can fold away. Nothing moves until you say so."
    Ticked on a first run.
  - **Track my AI usage.** "See how much of your plan you've used." Unticked on a first run. Ticking it opens
    the source list: **Claude**, with the trademark line wherever Claude is named.
- **Continue**, and **Not now**: closes without saving, and the screen opens again on the next launch.

**Continue applies, in order, one thing at a time:**

1. Saves the choices and turns the Desk on or off.
2. With drawers ticked and no drawers plan ever accepted, the first rule plan opens for review on the Desk
   (today's Sort my desktop now). Nothing moves until Apply.
3. With usage ticked, the widget opens: the sign-in card with no account, the meters with one.
4. Last, the welcome tour, built from what was picked (`WelcomeTour.StepsFor`, extended from Just the Desk's
   yes or no to the three choices). It waits until the review and the sign-in close.

**Kept off this screen:** the Claude Code add-ons (the MCP tools, the statusline, the mod). They stay on the
Claude Code page in Settings. Someone setting up a non-coder must not be offered developer add-ons.

## 3. Design it with an AI chat

For people with no coding agent. It follows the themes page's Copy agent prompt pattern and reuses the Desk's
design format (`DeskDesign`), its rules and the review bar that `propose_desk` already uses.

**Where:** the link under A nicer desktop on the setup screen, and a **Design it with an AI chat** button on
Settings, Desk.

**The prompt.** **Copy prompt** puts it on the clipboard. It tells the chat to:

1. Ask the user a few questions: the look, colours, what to show, what to sort into drawers.
2. Answer with exactly one design block in the format Sanduhr reads.

It lists only what this build's Desk can do: the pieces, anchors, sizes, ink, fonts, the message and weather,
and drawers with rules by kind, age or name. It carries the monitor layout (count, sizes, which is main) and
nothing personal: no file names, no account, no usage, no paths.

**Coming back.**

- A paste box with the live check, like the theme box. Problems are listed by field, and Preview stays
  disabled until there are none.
- **Preview** shows the design on the Desk through the existing review bar. **Apply** and **Undo** work there as
  they do for `propose_desk`.
- A pasted design passes exactly the rules an MCP design does, so a chat can do no more than Claude Code can.
- A paste holding several blocks uses the first good one and says so.
- A design naming a piece this build lacks is refused with the reason. Nothing applies partly.

**Drawers.** A chat may propose drawers and their rules, never file moves: it cannot see the files, and nothing
about them leaves the PC. The rules make their own plan, reviewed as usual.

**Naming.** The copy says "any AI chat" and names no product, so it adds no trademark surface.

**Limit.** Arrangement only. A chat cannot make new widgets.

## 4. Set up Sanduhr later

**Entries:**

- **Set up Sanduhr…** in the tray menu, near the top.
- A **Set up Sanduhr** button at the top of Settings, General.

Both open the same screen, with the ticks showing what is on now. It works signed in or not. Not now does not
appear here; the window has Cancel instead.

**Unticking, each said on the screen before Continue applies it:**

- **Desktop off:** the clock, message and weather hide. The layout is kept.
- **Drawers off:** today's turn-off (`DrawerService.TurnOff`). Every drawer file returns to the Desktop, with
  the existing "N files go back to your Desktop" confirmation and its open-file handling.
- **Usage off:** the widget hides and fetching stops. "Your account stays saved. Remove it in Settings,
  Accounts." With any Claude Code add-on installed, also: "The Claude Code add-ons stay installed. Remove them on
  the Claude Code page." Nothing is uninstalled.

**Ticking:**

- Desktop and drawers turn on as on a first run. Drawers runs its first review only if no plan was ever
  accepted.
- Usage resumes with the saved account, or shows the sign-in card.

**Existing users on upgrade:** nothing changes, and the screen does not open. A What's New card, "Choose what
Sanduhr shows", opens Set up Sanduhr. It replaces the drawers card plan 3 had planned.

## 5. Errors and edge cases

- **Not now on a first run:** nothing is saved. The screen opens on the next launch, and the tray has Set up
  Sanduhr… meanwhile.
- **Drawers off refused in part by an open file:** the turn-off result names the file. The setup screen reports
  what happened and does not claim it worked.
- **The Desk fails to start, or Explorer restarts:** today's Desk handling. Setup saves choices; it does not own
  the Desk window.
- **A settings file another build wrote:** `setup` is decoded entry by entry, like `drawers`. An unreadable
  entry falls back to the migration's answer for that entry, never to "nothing ticked".

## 6. Where this meets the drawers styling spec

Whatever drawers can do when a build ships is what the AI chat prompt lists and what a pasted design may set.
When the drawers styling and placement spec adds per-drawer styles or placement rules, they join the
`DeskDesign` drawers part and the prompt in the same change, and the prompt builder's test pins the list.

## 7. Testing

**Core (pure, unit-tested):**

- The setup model: choices in; `desk_enabled`, the desktop filter, the widget's visibility and the drawers
  switch out.
- The migration, in its four cases, plus an unreadable entry.
- The at-least-one rule.
- The prompt builder: what it lists for this build, and that it holds no file name, account, usage or path.
- The pasted-block reader: one block, several, none, a bad one.

**App:**

- The setup window builds, and every control has an accessible name.
- Usage off hides the widget and keeps the account.
- Drawers off runs the existing turn-off.
- The tray and Settings entries open the screen with the current ticks.
- The What's New card opens it.

**Live check, on a dev profile:**

1. A fresh first run with the default ticks: drawers review, Apply, tour.
2. Tick usage, sign in, untick usage: the widget goes and the account stays.
3. Paste a chat design, Preview, Apply, Undo.
4. Upgrade from a 4.0.1 profile, once with an account and once with Just the Desk: nothing changes.

## Order

1. Core: the setup model, the migration and the at-least-one rule.
2. The setup screen and the first-run route.
3. Set up Sanduhr later (the tray and Settings), and the What's New card.
4. Design it with an AI chat: the prompt builder, the paste box and the review.
5. Harness screenshots of the setup screen in obsidian and aurora.

## Release

The owner's order, 2026-10-31: this spec, then drawers styling and placement, then plan 3's other items. 4.5.0
ships drawers with its polish and setup together. 5.0 is the agent layer (specs 2 and 3) and the rename
(spec 4).
