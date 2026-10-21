# The agent-designed Desk — design (3.7.0, sub-projects A + B)

Windows only. 3.7.0 is the Store release; it ships with the Desk Launcher (D, built) and per-song looks
(C, next). Este's pitch: "Rainmeter on acid". Decisions: dashboard `GMVpx4duYZJ8kGOoDOzn` (direction),
`W7x5wo2nZ5d0lyfuZqDi` (3.7 scope), and the A + B design approved in chat on 2026-10-21.

## Intent

Say "design my Desk to match my wallpaper" to Claude Code. Claude reads the Desk and the wallpaper,
proposes a whole look (layout, font, ink, plate, Launcher, messages, widget theme), it draws live on the
real desktop, and the user Applies or Dismisses. Every applied look is saved and can be switched back to.

Decided (Este): review live on the desktop with a floating bar; any installed font, plus download links
for fonts the user lacks (Sanduhr never downloads); one proposal restyles the Desk and the widget theme
together; saved designs, ten kept; a direct-apply opt-in, off by default. Privacy: Sanduhr computes a
wallpaper summary locally and offers the wallpaper path; Claude Code's own file-read permission is the
consent to look at the image.

## A. The contract: `DeskDesign` (Core, pure)

A design is a **partial** set of changes; absent fields leave the current value alone.

| Field | Shape | Checked by |
|---|---|---|
| `layout` | piece key → `{ anchor: TL..BR, size: 0.6..1.6, hidden: bool }` | the eight anchors; known piece keys (`DeskArrangement.Known`) |
| `font` | family name | installed or bundled, else `needs_font` |
| `ink` | `auto` / `light` / `dark` / one hex / 2–4 hex gradient | `DeskInk` parsing; contrast rule |
| `plate`, `shadow` | bool | — |
| `launcher` | `LauncherModel` (orientation + ≤ 8 buttons) | `LauncherTarget` + Start-menu membership (no command lines, ever) |
| `messages` | Desk message lines + mode | `MessageProposal` |
| `looks` | per-song looks (filled by sub-project C) | C's rules |
| `theme` | a widget theme object | `ThemeLint` |
| `name`, `note` | short text (≤ 40 / ≤ 200 chars) | length, control characters |

- **`DeskDesign.Validate(design, context)`** returns `ok` / `rejected` (reasons per field) /
  `needs_font` (family + optional https link). `context` = installed fonts, the wallpaper summary, the
  Start-menu membership set.
- **Contrast rule:** for each placed piece, the ink's luminance against the wallpaper brightness under
  its anchor (B) must reach WCAG 3:1 (with the plate on, against the plate instead). Fail = rejected,
  naming the piece and anchor.
- **Font rule:** a family not installed and not bundled yields `needs_font`; the design is valid but
  cannot be applied until the font is installed. A link must be absolute `https`.
- **`DeskDesign.Merge(current, proposal)`** → the full resulting design; **`Diff(a, b)`** → the "what
  changed" lines shown in the bar.
- This one validator is the only new code `sanduhr-mcp` links (plus what it composes); the
  `TrustBoundaryTests` allowlist grows once and its purity scan covers the new files.

## B. The wallpaper summary (App, local)

- Reads the current wallpaper file the way `WallpaperInk` already does (per monitor, honouring the fit
  mode), downsampled. Computes: five dominant colors (hex + share), overall brightness, and for each of
  the eight anchors on each monitor: brightness (0–1) and busyness (edge density, 0–1).
- Pure math in Core (`WallpaperSummary.From(pixels, width, height, anchors)`), tested on fixtures; the
  file reading stays in the App.
- Cached per wallpaper path + last-write time; recomputed on wallpaper change.
- Never sent anywhere by Sanduhr. It leaves only as part of a `get_desk` answer to the local MCP client.

## MCP tools (`sanduhr-mcp`)

- **`get_desk`** → `{ design: <current, full>, pieces: [...keys], anchors: [...], fonts: { installed:
  [...], bundled: [...] }, wallpaper: { path, summary }, designs: [ { id, name, pinned, applied_at } ],
  direct: bool }`. The widget writes this as `desk-state.json` in `~\.sanduhr` (rewritten on change),
  the server reads it; no new channel.
- **`propose_desk`** `{ design, name?, note? }` → validates with `DeskDesign.Validate` first (refuses
  bad input without touching the widget), then hands the request to the widget through
  `desk-design-request.json` / `desk-design-result.json` (temp-then-rename, like the other handoffs) and
  returns `previewing` | `applied` (direct on) | `rejected` + reasons | `needs_font` + fonts |
  `queued` (widget not answering).
- `propose_theme` and `propose_desk_messages` keep their contracts; internally they become a design
  with only `theme` or `messages` set and use the same validator.

## The live review (App)

- On a valid proposal the widget **previews** it: the Desk and the widget redraw with the merged design
  in memory. Nothing is written.
- A **floating bar** (like Arrange mode's: top-center of the primary monitor, topmost while
  previewing): the design's name, Claude's note, the "what changed" list, any `needs_font` lines with
  their links, and **Apply** / **Dismiss**. Escape = Dismiss; Enter = Apply (disabled while a font is
  missing).
- **Dismiss** restores the pre-preview state instantly and answers `rejected` ("dismissed by the user").
- **Apply** saves the current Desk as a design, writes the new one (settings + theme), answers `applied`.
- A new proposal while previewing replaces the preview.
- **Settings ▸ Desk ▸ Let Claude change the Desk directly** (off by default): proposals apply without the
  bar (still saving the previous design first).

## Saved designs

- A design = the full `DeskDesign` + name + id + `applied_at` + `pinned`, stored as one JSON file per
  design in `%APPDATA%\Sanduhr\designs\` (package storage on the Store build). Launcher images are
  referenced by their hashed file names (kept while any design uses them).
- Every Apply first saves the current Desk ("Before <name>" when unnamed). Ten kept; the oldest unpinned
  pruned.
- **Settings ▸ Desk ▸ Designs**: the list (name, date), Switch to, Rename, Pin, Delete.
- **Put my Desk back** (Settings and the tray menu) switches to the design saved before the last Apply.
- Reset's Settings part removes the designs folder.

## Data and privacy

- New in `~\.sanduhr`: `desk-state.json` (the current design, installed font names, the wallpaper path
  and summary, design names), `desk-design-request.json`, `desk-design-result.json`.
- New private: `%APPDATA%\Sanduhr\designs\`.
- PRIVACY.md rows for each, plus: "Claude can open your wallpaper image only if you allow Claude Code to
  read that file." The hosted policy (hub #258) gains these rows before the 3.7.0 Store submission.
- No new capability, no new outbound destination. Font links are shown, never fetched.

## Testing

- Core: `DeskDesign` validation (each field; hostile inputs: unknown pieces/anchors, bad hex, command-like
  launcher targets, overlong text, control characters), the contrast rule against fixture summaries, the
  font rule, Merge and Diff, pruning; `WallpaperSummary` on fixture images (flat, gradient, busy corner).
- MCP: `get_desk` from a fixture `desk-state.json`; `propose_desk` validation refusals without a request
  file; the handoff round-trip with a fake widget (reading with retry, see the 3.6.0 flake); smoke-mcp
  lists both tools and rejects one bad design; `TrustBoundaryTests` with the new allowlist.
- WPF: the floating bar's automation names (Apply, Dismiss, the list); Designs list.
- Live on Neb: from a Claude Code session, "design my Desk for this wallpaper"; preview, Dismiss, then
  Apply; switch designs; Put my Desk back; a design naming a missing font shows its link and Apply waits.

## Out of scope

- Claude Code-side styling (statusline, meters band); per-song looks (sub-project C, which plugs into
  `looks`); sharing designs as files; Sanduhr downloading fonts; a design gallery or marketplace.
