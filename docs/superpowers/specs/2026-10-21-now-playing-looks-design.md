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

## As built (contract for the mod)

Source of truth: `windows-dotnet/src/Sanduhr.Core/NowPlayingLook.cs` (`SongKey`, `NowPlayingLooks`,
`NowPlayingLookCheck`, `NowPlayingLookProposal`); the tests pin every vector below.

**Song key.** `normalize(s)`: lowercase each Unicode scalar on its own (simple case mapping; U+0130 stays as
written); collapse each run of JavaScript `\s` whitespace to one space; trim; drop one trailing remaster mark, the
first match of the regular expression ` - remaster.*$| \(remaster[^)]*\)$` on the whole text; trim again. The key
is the FNV-1a 64-bit hash (offset `0xcbf29ce484222325`, prime `0x100000001b3`) of the UTF-8 bytes of
`normalize(title) + "\n" + normalize(artist)`, as 16 lowercase hex digits. A title that normalizes to nothing has no
key; the artist may be empty.

| title | artist | key |
|---|---|---|
| `Blinding Lights` | `The Weeknd` | `ad75f4c29077a9de` (so does `  Blinding   Lights  ` / `THE WEEKND`) |
| `Song` | `Artist` | `d2a7aa06c42e57bd` (so do `Song - Remastered 2011` and `Song (Remastered 2009)`) |
| `Café del Mar` | `Energy 52` | `245e2ded93cf9f31` (so does `CAFÉ DEL MAR` / `energy 52`) |
| `Untitled` | (empty) | `5f31c64688d31616` |
| `Hey Jude - Remastered 2015` | `The Beatles` | `b0ccc496e92995e4` |
| `Remaster Me` | `X` | `a31dfc67c5b9c43a` (no space-hyphen-space before it: not a mark) |
| `A - Remastered` | `B` | `e5beb1190415e670` |
| `x` | `y` | `be74b5197ee0cbea` |

Raw FNV-1a 64: `""` is `cbf29ce484222325`, `"a"` is `af63dc4c8601ec8c`, `"foobar"` is `85944171f73967e8`.

**A look** is `{ink, style, mood?}`: `ink` 1 to 4 `#rrggbb`, `style` one of `plain bold italic bolditalic
smallcaps` (the mod maps its other fonts onto these, or onto `plain`), `mood` up to 40 characters. A seeded look
(the file below carries it as `source: "seeded"`; the mod never has to compute one): palette `h mod 8` over the
mod's eight, style `[smallcaps, italic, bold, bolditalic][(h div 8) mod 4]`, `h` the key as a 64-bit number.

**`~/.sanduhr/now-playing-look.json`** (Sanduhr writes, the mod reads), only while the MCP integration is installed
and Style what's playing is on, removed otherwise:
`{"schema_version":1,"key":"ad75f4c29077a9de","look":{"ink":["#ff2a6d","#05d9e8"],"style":"smallcaps","mood":"neon"},"source":"saved"}`
(`source` is `saved` or `seeded`). No title, no artist. When its `key` equals the mod's key for the song it is
playing, the mod uses `look` (source `sanduhr`) instead of asking its model.

**`~/.sanduhr/now-playing-look-offer.json`** (the mod writes, atomically through a temp name and a rename; Sanduhr
reads and deletes it): `{"key":"…","title":"…","artist":"…","look":{"ink":[…],"style":"…","mood":"…"}}`, with an
optional `"schema_version":1`. Write it only for a song whose key was not the saved one. `key` must be the key of
`title` and `artist`, or the offer is dropped; an offer older than ten minutes is dropped. Sanduhr treats it as a
Claude proposal (a card in Settings ▸ Now playing ▸ Looks, or saved directly) and never answers it.

**Contrast.** Every stop must keep 3:1 against what is behind the now-playing piece (the wallpaper at its anchor,
or the plate laid over it); a look that does not is refused with "color #rrggbb is too dark for this spot
(r:1, needs 3.0:1)" (or "too light"). Seeded looks are not judged.

**Saved looks** are `%APPDATA%\Sanduhr\now-playing-looks.json` (500 songs, the oldest going first), private to the
widget; the server never reads them.
