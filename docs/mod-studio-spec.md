# Mod Studio

Spec, 2026-10-13. Follows item 64 (the Mods page) and item 71 (Mods & Config, `docs/mods-and-config-spec.md`). Research input: `docs/superpowers/research/2026-10-06-claude-code-mods.md`.

Evidence tags as in the Mods & Config spec: [docs <url>], [repo <path>], [local <observation>], [unverified].

## Verdict: a sheet for working on one mod, with a live preview, and a shorter Catalog to find it in

The owner, 2026-10-13: "I want to be able to edit and add mods. This gives us a better preview of what we get. Maybe there should be a modal that pops up to work on mods." And: "When showing the mods, plugins etc, we could have a shorter list if we just had the item name and a collapsable."

Today Mods & Config shows Sanduhr's meters mod as a row per folder with On, Update and Remove [repo mac/Sources/Sanduhr/Views/ModsOwnSettings.swift]. There is no preview, nothing about the band can be changed there, and there is no way to start a new mod. What the band can be told lives in three places nobody sees together:

- **Its looks** (ink, gradient, bold, italic, dim, letters for the session, weekly and reset segments) reach the band through `band.json` `meters.styles`, from the `bandMeterStyles` default [repo mac/Sources/Sanduhr/Services/BandFile.swift]. Only the Combine sheet on Settings, Claude Code writes that default, and only for a combined statusline [repo mac/Sources/Sanduhr/Views/IntegrationsSettings.swift:183, 217]. A folder with Sanduhr's line alone, or no statusline, has no way to style its band.
- **Its options** (`bars`: both, session or weekly; `style`: compact or full; `label`; `motion`: on or off) are read from the mod's `register(on, options)` [repo mac/integrations/mods/sanduhr-meters/hooks/register.tsx:399-405]. Claude Code fills them from `pluginConfigs["sanduhr-meters@inline"]` in a settings file [docs https://code.claude.com/docs/en/plugins/mods/reference#settings-and-environment-variables, via the research doc]. Sanduhr never writes it, so every band runs on the defaults.
- **Its watchers rows** are switched on Watchers.

**Mod Studio** is a sheet opened from a mod's card. It shows the band drawn by Sanduhr with demo numbers on one side and everything that shapes it on the other, and nothing is written until Save. The same sheet starts a new mod. The Catalog gets a compact form, one line per item, so the page stops being a wall of cards.

## Principles

- **Preview before write.** Every control changes the preview first; Save writes; Revert drops the draft. Closing with unsaved changes asks.
- **Writes go through the existing paths.** Looks through `BandFileWriter.setMeterStyles`; options through a receipt on the folder's `settings.json` with exact undo, as the meters switch already does; on, off, update and remove through the existing installer. No new way to touch a Claude Code folder.
- **The preview is honest.** It is drawn from the same rules the mod runs. Where Sanduhr cannot know (a third-party mod's look), the Studio says so and shows what the mod can do instead of a guess.
- **Demo numbers only.** The preview never reads a real snapshot, so it is safe for screenshots and works signed out.
- **The wall holds.** In demo mode work folders don't appear in the Studio's folder list. The Build with Claude prompt names no folder path and no setting value.

## The sheet

Opened by **Open Studio…** on the meters mod's card (Mods & Config, and its Catalog card), and by **New Mod…** at the top of the Catalog. About 860 by 600, resizable, one per window.

```
┌ Sanduhr meters ─────────────────────────────── v2.19.0 · on in 2 of 3 folders ┐
│ ┌ Preview ──────────────────────────────┐  [ Look | Layout | Folders | About ]  │
│ │ Personal  5h ████████░░░░ 62% ▲      │                                        │
│ │           wk ███░░░░░░░░░ 24%        │  Session   [brush]  ▓▓▓ gradient       │
│ │           resets 1h 12m              │  Weekly    [brush]  ▓   one color      │
│ │ ─ prompt ──────────────────────────  │  Resets    [brush]  default            │
│ └──────────────────────────────────────┘                                        │
│  Show:   (Calm) (Near a line) (Crossing) (Before reset) (Nearly full) (Watchers)│
│  Width: ──●────  Reduce Motion: off                                             │
│                                                         [Revert]  [Save]        │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Preview

- The band as a session draws it: bars, pace mark, reset words, label, and watchers rows when **Watchers** is picked. Monospaced, on a terminal-dark background, with a width slider (narrow terminals wrap differently).
- **Show** (2026-10-13: the owner found "Moment" unclear) sets the demo numbers and play the effect that goes with them: **Calm** (no effect), **Near a line** (amber), **Crossing** (the one-shot sweep when a meter crosses a warning line), **Before reset** (the shimmer), **Nearly full** (the glow), **Watchers** (two demo rows, one waiting with its pulse). Clicking the one showing replays it; Crossing replays every three seconds.
- **Reduce Motion** follows macOS and can be flipped in the preview alone, since the band honors it through `band.json` `reduce_motion`.
- Drawn natively in SwiftUI from a Swift port of the band's pure functions: `bandFor`, `meterText`, `resetWords`, `gradient`, `lightAt`, `glowLevel` and the moving-light timing [repo mac/integrations/mods/sanduhr-meters/hooks/meters.ts, band.ts]. To keep the two in step, the mod's tests (`claude plugin test`) write golden fixtures (inputs and the cells they produce, color per character) to `mac/integrations/mods/sanduhr-meters/fixtures/`, and a Swift test renders the same inputs and compares. A change to either side that the other doesn't match fails CI.

### Look

The session, weekly and reset segments, each with the brush the statusline chips already use (ink: one color or 2 to 4 gradient stops, bold, italic, dim, letters, Reset to its own look) [repo mac/Sources/Sanduhr/Views/StatuslineSegmentPicker.swift]. Save writes them with `setMeterStyles`, whatever kind of statusline the folders have. That closes today's gap. The Combine sheet keeps writing the same default, so the two stay one setting: a change in either shows in the other.

Looks are one setting for every folder because `band.json` is one file. The tab says so.

### Layout

The mod's options: **Bars** (both, session, weekly), **Style** (compact, full), **Label** (a few words before the bars; empty shows none, as the manifest says), **Motion** (on, off). Each changes the preview.

Options are per folder in Claude Code (`pluginConfigs` lives in a settings file), so Save asks where: **This folder** (a picker, the folder the sheet was opened for first) or **Every folder where it's on**. Each folder written gets a receipt; Remove on the folder's row restores the file byte-exact, as the meters switch does. Folders whose `pluginConfigs` already has a value Sanduhr didn't write keep it, and Save says which.

### Folders

The rows that sit on Mods & Config today, moved here with no change in behavior: each Claude Code folder with its label and side, On, the version in use, **Update** with its capability question, **Remove**, the project-override note and the receipts' undo. Mods & Config keeps a one-line summary per mod ("on in 2 of 3 folders") and **Open Studio…**.

### About

What the mod can do, from `claude plugin validate --json` (hooks, calls, surfaces, files; nothing is run), its version and where its files are, and what it reads (the snapshot file for its folder, `band.json`) and never does (no network, no model calls, no writes). This is the risk card item 64 describes, in the place someone looks before turning a mod on.

## Adding a mod

**New Mod…** opens the Studio on an empty mod with two ways in.

1. **Build with Claude…** (from the 2026-10-08 spec addition). A field takes the idea in a sentence ("a band that shows my CI runs"). **Copy Prompt** puts a ready prompt on the clipboard for Claude Code or Claude Desktop. It covers what a mod is (a plugin folder with `.claude-plugin/plugin.json` and a module exporting `register(on, options)`), what it may draw, the effects grammar and `band.json` style rules Sanduhr already speaks, the rules (no network unless asked, no secrets, read-only by default, tests with `claude plugin test`), and, once `propose_mod` exists, to finish with it. It names no folder path and carries no settings value. Until `propose_mod` ships, the prompt tells the agent to build the mod in a session folder, where step 2 finds it.
2. **Adopt a mod built in a session** (item 64 slice 3). The Studio lists mod folders it finds in session dev-mod folders and `--plugin-dir` folders that aren't managed yet. **Adopt** copies one into a managed, versioned folder under Application Support, runs `claude plugin validate --json`, shows the About tab and then Folders, where turning it on is the usual consent and receipt.

A mod that isn't Sanduhr's gets Folders and About, and **Layout** only when its `plugin.json` declares an options schema [unverified: whether mods declare one]. It gets no native preview: Sanduhr can't draw a look it doesn't know. **Try in a session** opens Terminal on a scratch folder with `claude --plugin-dir <the staged copy>`. Because that runs the mod's code, it is behind the About tab's capability report and a confirm.

**Agents proposing mods** (`propose_mod`, `propose_install`, `list_catalog`, item 64 slice 4 and the 2026-10-08 addition) arrive last: a proposal lands as a **Proposed** card, and its Studio opens on About with **Install in…**. The **Let agents propose mods** switch is off by default, and nothing runs before approval.

## The compact Catalog

- **Compact by default.** Each item is one line: kind icon, name, a dot per folder that has it in that folder's state color (on, off, declared), and the Needs-a-look mark when it has one. Click or press Return to expand that line into today's full card in place. Space or the arrow toggles; Left and Right collapse and expand, as a list does.
- **Expand All** and **Collapse All** at the top, beside the existing group controls. Expanded lines stay expanded while the window is open, like the groups.
- **Search** keeps lines compact and highlights the matched name; a match inside a plugin's contents (a skill name behind **Show**) expands that line so the match is visible.
- A mod's line has **Open Studio…** on its trailing edge, visible on hover and to VoiceOver as an action.
- Compare uses the same lines.

## Slices

Each ships on its own and is useful without the next.

| Slice | What | Writes |
|---|---|---|
| A | Compact Catalog | nothing |
| B | Studio shell, Preview with its Show buttons, Look tab; looks saved for every statusline kind | `bandMeterStyles` only |
| C | Folders tab (move from the page) and About tab (built 2026-10-13: the page keeps the on-line with **Folders…** and **Open Studio…**) | none new (existing installer) |
| D | Layout tab: options per folder through a `pluginConfigs` receipt (built 2026-10-13) | folder `settings.json`, with receipt |
| E | New Mod: Build with Claude prompt (built 2026-10-13: a small **New Mod** sheet from the Catalog, not the Studio, until slice F gives the Studio an empty mod to open on) | clipboard only |
| F | Adopt a session-built mod; Try in a session | managed mod folder; consent per folder |
| G | `propose_mod`, `propose_install`, `list_catalog` behind **Let agents propose mods** | staged proposals |

A and B are the owner's immediate ask: a shorter list and a real preview with editing.

## Risks

- **Preview drift.** Two implementations of the band can disagree. The shared golden fixtures are the guard. The Studio also carries a one-line "Drawn by Sanduhr with demo numbers; a session draws the same rules" so nobody mistakes it for a live session.
- **Options written into a live folder.** `settings.json` changes while Claude Code runs. Mods reload in place in interactive sessions [docs, via the research doc], but whether a `pluginConfigs` change reaches a running session without restart is [unverified]. Save says "Applies to new sessions" until checked.
- **`pluginConfigs` precedence.** A project's `.claude/settings.json` can set the same key and win. Folders shows the project-override note it shows for the switch today, and Layout says when a project overrides an option (counted, never named for work folders).
- **A third-party mod run by Try in a session** has the user's full authority. It is behind the capability report and a confirm, and never offered for a Proposed card before approval.
- **Signed-out dev checks** blank every live band (each linked seat's snapshot is written signed out). The Studio's preview doesn't depend on the snapshot, so on-screen checks of slice B need no key.

## Open questions

1. ~~The exact key and shape.~~ Answered 2026-10-13: `pluginConfigs["sanduhr-meters@inline"].options.<field>`, read from user or managed settings only; project and local entries are ignored since v2.1.207 [docs https://code.claude.com/docs/en/settings-reference#pluginconfigs]. So the project-override risk above doesn't apply to options. Claude Code's `/config` writes the same member, so Save writes only where there is none or Sanduhr's own is still as written, and leaves any other.
2. Does a running session pick up a `pluginConfigs` change? Not checked live. The mod copies its options in `register`, which runs when the mod loads, so Layout says sessions started after Save use them and a running one keeps its layout. Whether `/reload-plugins` re-reads them is [unverified].
3. Do mods declare an options schema in `plugin.json` that the Studio could build a Layout tab from for any mod?
4. Should looks become per folder? It would need `band.json` to carry styles per account ref. Not proposed now: one look everywhere matches the statusline's Combine styles.

## Acceptance

- The Catalog opens compact; an expanded line shows the full card; search finds a skill inside a plugin and opens that line.
- From the meters mod's card, the Studio opens; every Show button plays its effect in the preview; a gradient on Session shows in the preview before Save, and in a running Claude Code session's band after Save, in a folder whose statusline is Sanduhr's line alone.
- Bars set to weekly for one folder appears in that folder's new sessions only, and Remove on that folder's row returns its `settings.json` byte-identical.
- Build with Claude copies a prompt containing no path under the home folder and no settings value.
- Nothing in the Studio reads a real snapshot; demo mode shows no work folder.

## Verify

swift-testing for the band port against the golden fixtures, the options receipt (write, undo byte-exact, a value not Sanduhr's left alone), the prompt's contents, and the compact Catalog's expand and search state. `claude plugin test` for the fixtures' generator, beside the band's existing tests. By hand: the owner's two seats, one with Sanduhr's line alone, one combined; each Show button; Save and Revert; a live session's band after Save.

## Checklist entry

Item 75 in `docs/checklist.md`.
