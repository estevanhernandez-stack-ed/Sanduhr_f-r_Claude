# A look per song — design (3.7.0, sub-project C)

Windows. Mac parity with item 65c (`propose_now_playing_looks`, `mac/integrations/sanduhr_mcp.py`,
`docs/mac-smoke-test.md` §38) plus a bridge so Este's Claude Code now-playing mod
(`~/.claude-personal/mods/now-playing`) and the Desk wear the same look for the same song.
Decisions: dashboard `W7x5wo2nZ5d0lyfuZqDi` (3.7 scope), `GMVpx4duYZJ8kGOoDOzn` (direction).

## What a look is

`{ ink: 1–4 hex stops, style: plain | bold | italic | bolditalic | smallcaps, mood: ≤ 40 chars }`.
The palettes and styles match the mod's (`hooks/theme.ts`: eight palettes synthwave, sunset, ocean,
aurora, ember, bubblegum, toxic, gold; its font names map onto the Desk's `{font:…}` styles).

A song's **key** = FNV-1a 64-bit hex of `normalize(title) + "\n" + normalize(artist)`, where normalize
= trim, collapse whitespace, lowercase invariant, drop a trailing " - remastered…"/"(remastered…)"
suffix. The same key in Sanduhr (Core, tested) and in the mod, so a song wears one look in every app
and on both surfaces. The rule is written down in this spec and in the mod.

## Sanduhr

- **Settings ▸ Now playing ▸ Looks:** **Style what's playing** (off by default), **Let Claude style
  songs directly** (off by default), "N saved looks" / "No saved looks", **Clear Looks…** (confirm).
- **Style on:** the Desk's now-playing line draws the title in the look's gradient and letter style;
  the position bar and the visualizer bars take the gradient; the ▶ glyph stays plain. An unseen song
  gets a **seeded** look (palette and style picked from the key), so every song has one at once.
- **Saved looks** live in `%APPDATA%\Sanduhr\now-playing-looks.json` (private; package storage on the
  Store build): key → look + title + artist (titles are stored only for looks you keep). Reset's
  Settings part removes it.
- **Contrast:** a look whose stops fall under 3:1 against the Desk background at the now-playing
  piece (wallpaper summary, or the plate when on) is refused with the reason ("too dark for this
  spot"); with OLED care, stops render through the white cap like all ink.

## MCP

- **`propose_now_playing_looks`** `{ looks: [ { title, artist, ink, style, mood } ] (1–20), note? }`
  → validated first (no request file on refusal) → `pending_approval` (a card on Settings ▸ Now playing:
  "Claude suggested looks for N songs", each song drawn in its look on black with its mood, Save /
  Dismiss) or `applied` with `looks_saved` (direct on); `style_on` reported so Claude knows whether
  looks show. Dismiss answers `rejected` ("dismissed by the user").
- **`DeskDesign.looks`** (refused as "not supported yet" in phases 1–2) now accepts the same entries,
  so a whole Desk design can carry song looks.
- Both go through one Core validator (`NowPlayingLook.Validate`), linked into `sanduhr-mcp`; report any
  allowlist change.

## The bridge with the now-playing mod

- While the MCP integration is installed and Style what's playing is on, Sanduhr writes
  `~\.sanduhr\now-playing-look.json` = `{ schema_version, key, look, source: saved | seeded }` for the
  song playing now (no title, no artist — the key is enough for the mod, which knows its own song).
  Removed when nothing plays, when style is off, and with the integration.
- **The mod** (`hooks/register.tsx`, `hooks/theme.ts`): computes the same key; if
  `now-playing-look.json` matches, it uses that look (source `sanduhr`) instead of asking the model;
  when the model picks a look for a song Sanduhr has no saved look for, the mod writes
  `~\.sanduhr\now-playing-look-offer.json` `{ key, title, artist, look }`, which Sanduhr treats as a
  Claude proposal (a card, or saved directly with "Let Claude style songs directly").
- The mod lives outside this repo (Este's config and the `sanduhr-mods` marketplace copy); its change
  is a separate step with Este's nod.

## Testing

- Core: the key (normalization cases, FNV-1a vectors shared with the mod's test), look validation
  (stops, styles, mood, hostile input), seeding determinism, the contrast rule, store round-trip.
- MCP: the tool's refusals without a request file; the handoff with a fake widget (retrying reads);
  smoke-mcp lists it and rejects one bad look.
- WPF: the Looks section's automation names; the suggestion card.
- Live: play a song with Style on (seeded look on the Desk), ask Claude for looks for it, Save, the
  Desk changes at once; the mod's band shows the same look; Clear Looks; direct on.
