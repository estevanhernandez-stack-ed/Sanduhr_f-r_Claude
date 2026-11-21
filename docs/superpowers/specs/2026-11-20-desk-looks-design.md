# Ready-made Desk looks and the neon glow: design (5.0 spec 5)

**Status:** design approved by Este, 2026-11-20; amended with the plan (2026-11-20-desk-looks.md) where the code map changed it. Windows only.
**Umbrella:** `2026-11-17-windows-5-0-umbrella.md`. **Builds on:** `2026-10-21-desk-design-design.md`
(the design format, the validator, saved designs and Put my Desk back).
**Decisions:** dashboard `PxXvRHTsj8Y2ITITu9M4` (looks lead the customizer track), `O2UrEEfYnaPsd9J4KkPl`
(no wallpaper in 5.0), `BOUBKCNZzCtnL5UyXQfG` (a look owns the arrangement), `2s2G9TcNNNNp0UZRmefB` (the
glow), `RvpJKjkAtxWWAf88HYDT` (fonts), `nGajG9ZQqaTT89G2kyCT` (six looks), `Mf7hA6BwUHJ9coDpzMdI` (swatch
cards).

## 1. Intent

A desktop customizer judges the Desk in the first minute and may never sign in to an AI plan. Today a new
user gets the default layout (a clock and the Claude line, bottom-left) and five ink colors. No complete
look ships.

5.0 gives them a gallery of six complete looks, applied in one click from Settings and from setup, undone
by Put my Desk back. It also adds a neon glow on the Desk's ink, which a look can turn on.

Success: a new user has a Desk worth a screenshot in under a minute, and the trailer can flip through
looks.

## 2. Decided

- **One click applies.** No review bar: the bar exists to check an agent's work, and these looks are ours.
- **Looks ship inside the app.** Nothing is downloaded.
- **No wallpaper in 5.0.** A look adapts to the user's wallpaper. Looks that bring their own wallpaper
  come after 5.0 (dashboard task `task-1795192163146-ab9ypxtf5`), so the look file leaves room for one.
- **A look owns the arrangement and stays out of the user's content.** It decides which pieces show,
  where and how big, plus font, ink, plate, shadow and glow. Launcher buttons, message lines, the weather
  place and each piece's source are never touched.
- **Fonts:** only fonts that ship with Windows, plus the two the app bundles (EsteFont Pro, EsteFont 26).
- **Six looks:** Quiet corner, Ice, Marquee, Terminal, Editorial, Dashboard.
- **Swatch cards.** A card suggests the look; the real Desk is the preview.
- **The glow** is one switch for the whole Desk, static, in the ink's own colors, with a strength slider.
  Glow and the drop shadow are either-or.

## 3. A look

### The file

Six JSON files, embedded in the app. One per look:

```json
{
  "schema_version": 1,
  "id": "marquee",
  "name": "Marquee",
  "blurb": "Clock up top, neon glow.",
  "font_fallback": "Segoe UI",
  "design": { }
}
```

- `design` is a design in exactly the format `propose_desk` accepts, so a look can be auditioned on a real
  Desk through `propose_desk` unchanged.
- `font_fallback` is used when the design's font is not on this PC. It must be a font present on every
  Windows the package supports (build 17763 and later), or a bundled one.
- The wrapper is where a wallpaper goes after 5.0. An unknown wrapper field is ignored, so a later file
  still loads; an unknown field inside `design` is refused, as it is today.
- `id` is lowercase letters and hyphens, unique, and is the file's name.

### What a look's design must and must not carry

**Must carry:**

- `layout`, naming every piece Windows draws, each as shown (with its anchor and size) or hidden. Windows
  draws every word in `DeskArrangement.Known` except `meetings`.
- `font`, `ink`, `plate`, `shadow`, `glow`.
- `follows_theme: false`. A followed widget theme overrides the Desk's ink, so a look has to switch it off.

**Must not carry:**

- `launcher`, `messages`, `looks`, `theme`, `weather`. Those are the user's content.
- `monitor` on any piece. Each piece stays on the screen the user put it on.
- `meetings`, a drawer word, or `name` and `note` (the wrapper names the look).

The pieces at one anchor stack in the order the file lists them.

Drawers are not part of a look and stay where they are, as they do for every design.

### The six, as starting compositions

Positions and sizes are tuned by auditioning each look on Este's Desk through `propose_desk`. The tests
pin the rules above, not these positions.

| Look | Font (fallback) | Ink | Plate | Glow | Arrangement |
|---|---|---|---|---|---|
| Quiet corner | Corbel (Segoe UI) | `auto` | off | off | Small clock, Claude line and now playing, all bottom-left |
| Ice | EsteFont Pro | Ice gradient | off | off | Large clock bottom-left, message above it, weather top-right |
| Marquee | Bahnschrift (Segoe UI) | Marquee amber, `#ffd08a` | off | on | Large clock top-center, message under it, now playing bottom-center |
| Terminal | Consolas | one green, starting at `#7cfc9a` | off | off | A left column: clock, Claude line, meters and telemetry as ticks |
| Editorial | Georgia | `light` | off | off | Large serif clock top-left, message beside it as a pull quote, weather bottom-left |
| Dashboard | Segoe UI | `auto` | on | off | A right column: clock, meters and telemetry as rings, watchers, launcher along the bottom |

Marquee keeps the glow and no drop shadow; the other five keep the drop shadow and no glow.

WPF lists Segoe UI as one family; weights are not families there, so no look names Segoe UI Light or
Segoe UI Semibold (checked 2026-11-20). Every font a look or its fallback names is in
`DeskLook.WindowsFonts`, the families that ship with every supported Windows, or is bundled; a test holds
that, and another checks each name against this PC's own list.

## 4. Fitting a look to this PC

`DeskLookFit` is a pure function in Core: a look, the wallpaper summary and the installed fonts in, a
fitted design and a list of notes out. It runs before a look is drawn on a card and before it is applied.

1. **Font.** If the design's font is neither installed nor bundled, the design takes `font_fallback`.
   Note: "Uses Segoe UI on this PC." When the fallback is missing too, the design takes the system
   font, with its note.
2. **Ink.** For each shown piece that takes the ink, the contrast the validator would judge:
   - As designed: keep it when every piece reaches 3:1.
   - Otherwise turn the plate on and judge again. Note: "Uses its plate on your wallpaper."
   - Otherwise set the ink to `auto`, with the plate as the look designed it. Note: "Uses automatic ink on
     your wallpaper."
3. **No wallpaper summary** (the picture could not be read): the look is used as designed, which is what
   the validator does with no summary.

The fit is judged over the Desk as it is, so the user's labelled Launcher buttons count.

The contrast judgement is the validator's own. `DeskDesignValidator.CheckContrast` is split so that the
part that judges one design against one wallpaper is callable from `DeskLookFit`; there is one
implementation, not two.

**A fitted look always passes `DeskDesign.Validate`** against the same context. A test holds that for
every look over every fixture wallpaper.

A look's ink of `auto` is never adjusted: it follows the wallpaper by definition.

## 5. Applying a look

`DeskDesignService.ApplyLook(id)`, a new public method beside `SwitchTo`:

1. Disposed, or Reset has run: "Sanduhr is closing." An unknown id: "That look is gone." A drawers review
   is open: "Finish the drawers review on your Desk first." A design under review is dismissed.
2. Fit the look to this PC.
3. The Desk already is exactly that fit: with the Desk on, "<name> is your Desk already."; with it off,
   switch it on and answer "<name> is on your Desk. The Desk was switched on."
4. Rule A: unless the Desk is an untouched built-in look, save it first ("Before <name>", or the saved
   design that already is that Desk). A save that fails stops here: "The Desk could not be saved first, so
   nothing was changed."
5. Write the fitted design through the existing `WriteDesign`, its layout in the file's own order, each
   piece on the monitor the user gave it (`DeskLookFit.LayoutOver`), not merged.
6. Switch the Desk on if it was off: `desk_enabled`, and setup's "A nicer desktop".
7. Rule B: no `KeepApplied`. Point Put my Desk back at what step 4 saved, when it saved; tell the host;
   answer "<name> is on your Desk." and, when step 6 did anything, " The Desk was switched on."

A failure to save the current Desk stops before anything is written, as it does for a design today.

### Two rules so browsing does not make a mess

Today every apply saves a "Before <name>" copy, and ten saved designs are kept with the oldest unpinned
pruned. Trying six looks would save six copies and could push out designs the user saved on purpose.

- **Rule A: flipping through looks keeps one backup.** If the Desk is an untouched built-in look when
  another look is applied, nothing new is saved, and Put my Desk back keeps pointing where it did. So it
  still returns to the Desk from before the first look.
- **Rule B: a built-in look takes no saved-design slot.** `ApplyLook` does not call `KeepApplied`. A look
  the user then changes is their own Desk and is saved like any other when the next change replaces it.

**"Untouched"** is judged on the fields a look carries, and nothing else: the user's Launcher, messages
and sources never count. The current Desk is an untouched look when its layout, shadow, glow and
`follows_theme` equal the look's, its font is the look's font or its fallback, its ink is the look's ink
or `auto`, and its plate is either value. Those are exactly the things fitting may change, so a look
applied over one wallpaper is still recognised after the wallpaper changes.

**`SwitchTo` follows rule A too**, and Put my Desk back with it. Leaving an untouched look for a saved
design saves nothing and leaves the pointer where it was. One visible difference from today: after Put
my Desk back returns from a look, pressing it again answers "Your Desk is already as it was." where today
a second press toggles back. Between two of the user's own designs it toggles as before.

### Switching the Desk on

A design does not cover `desk_enabled`. `ApplyLook` switches the Desk on because one click should produce
a Desk. Put my Desk back does not switch it off again; the Desk switch stays the user's.

## 6. The gallery

### In Settings

A new section, **Looks**, on Settings, Desk, under the Desk switch and above Pieces. Its jump link is
first. No new page.

- A heading line: "Looks", with **Put my Desk back** at its right, enabled when there is a Desk to go
  back to. It is the same command as the button in Designs and the tray item.
- Six cards in a wrapping row.
- A status line under the cards for the last action's answer. It is a live region, so a screen reader
  speaks it.

Settings search finds the section under "looks", "gallery", "presets" and "styles".

### A card

- **Top:** a crop of the user's wallpaper. Over it, the look's name in the fitted look's font and ink,
  with its plate and glow when it has them, standing where the look puts its clock, and the piece map in
  the same ink. The map is drawn by the
  layout code `DeskLayoutMap` already uses (`DeskArrangement.Stacks`, `DeskGeometry`), for one screen.
- **Bottom:** the blurb, then at most two notes, in this order: the fit notes from section 4, then what a
  shown piece needs.
- **The look that is the Desk now** carries "On your Desk" and a check mark.

**What a shown piece needs.** One note, naming every piece the look shows that has nothing to draw:
"Weather needs a place." "The Launcher needs buttons." "Watchers needs a watcher." Every sentence is kept.
It is drawn as one wrapping run of links under the card, outside the button, each sentence a link to that
piece's Settings page. It shows when the fit's notes left a line free. The Claude line and the meters are
not listed: they say so themselves on the Desk when no one is signed in.

**The wallpaper crop.** Read through the reader `WallpaperInk` already uses, scaled down once and held in
memory; made again when the wallpaper changes. Nothing is written to disk. When the picture cannot be read
(a slideshow between pictures, a solid color, a file that is gone), the card's top is the wallpaper
summary's first dominant color, or the theme's background when there is no summary.

### Cards are buttons

The theme gallery is a list where selecting an item applies it. For looks, arrow keys across such a list
would rewrite the Desk on every step. So each card is a button:

- Tab and the arrow keys move focus only. Enter, Space, a click or UI Automation's Invoke applies.
- Tab reaches the row of cards as one stop, and the arrow keys move between the cards and the links under
  them. Neither applies a look. The Settings redesign's guard "Tab reaches Pieces from the Desk page's
  first stop in under 10" is re-pinned to 11, because the Looks section, with its own jump link and Put my
  Desk back, sits above Pieces. This is the owner's to confirm.
- Accessible name: "<name> look. <blurb> <notes>" and, on the current one, "On your Desk." The name
  changes when the state does.
- The row has an accessible name, "Ready-made looks".
- Every color on a card passes the light-mode and dark-mode checks the Settings pages already pass.

### In setup

Under the "A nicer desktop" tile, beside "Design it with your AI chat": **Start with a look**, a row of
the six cards at a smaller size (name over the wallpaper crop and the blurb; no notes).

- The six cards are one strip, a single row scrolled sideways (the mouse wheel over it scrolls it
  sideways), not a wrapped row. Wrapped two per row, they added about 500 DIPs to a fixed-width,
  non-resizable window and pushed Continue off a 1280 by 720 DIP screen. The setup window is capped at the
  screen's work area: its choices sit in a scroll area and Continue sits outside it.
- Enabled only while the tile is ticked.
- Picking a card marks it; picking it again clears it. Nothing changes until Continue, like every other
  setup choice.
- Continue with a look picked applies it after the Desk is switched on. With none picked, setup behaves
  as it does today.
- `SetupChoices` gains the picked look's id.

## 7. The neon glow

### Drawing

Behind each piece's ink sits a blurred copy of that ink: a `VisualBrush` of the piece's ink painted on a
shape of the same size, under a `BlurEffect`, below the ink in z-order. A gradient ink therefore glows as
a gradient. (`DropShadowEffect`, which the message's glow uses, glows in one color only.)

- **What glows:** everything that takes the ink: text, and the meters' bars, slim bars, rings and ticks.
  The message glows too, in its own color, drawn the same way.
- **What does not:** Launcher icons, the companion's art, drawers and the Claude Code glow pill.
- **Strength**, 0.2 to 1.0, default 0.6: the blur radius runs from 6 to 22 DIPs and the copy's opacity
  from 0.45 to 0.9.
- **Room.** An island's window has 16 DIPs of padding (`DesktopLayerHost.Pad`). While the glow is on, the
  padding is the larger of 16 and the blur radius plus 4, so the glow is not clipped: 16 at strength 0.2 and
  26 at 1.0. With the glow off, nothing changes.
- **With the plate on,** the glow draws between the plate and the ink.
- **OLED care** dims and drifts the glow with the piece, because the glow is part of the piece.
- The glow does not count toward the contrast rule, which judges the ink against the wallpaper.

### Under the desktop icons

With "Desktop icons" drawing over the Desk, `UnderIconsHost` renders each island in software through a
`RenderTargetBitmap` and copies the pixels. Effects cost more there, and a clock redraws every second or
every minute.

The plan's first task measures it: the time to capture the clock's island with the glow at strength 1.0.

- **Under 8 ms:** the Desk under the icons draws the same glow.
- **8 ms or more:** the Desk under the icons draws the glow with a `DropShadowEffect` at depth 0 in the
  ink's middle color, and the normal Desk keeps the blurred copy. Desk Look says so beside the switch:
  "Under the desktop icons the glow is one color."

**Measured, 2026-11-20.** One software capture of the clock's island at strength 1.0, median of 21, three
runs on the owner's PC, the test process reading the display scale as 1.00: no glow 1.95 ms, blurred copy
8.54 ms, one-color glow 6.34 ms. The blurred copy is over the 8 ms budget, so
`DeskGlowLayer.UnderIconsOneColor` is `true`: under the desktop icons the glow is one color, above them it
is the blurred copy. A test process is not DPI aware, so a scaled display has more pixels and costs the
same or more.

The glow covers a followed widget theme's ink. Launcher labels glow in one color. Drawer labels neither
glow nor keep their shadow while the glow is on. `desk_ink` is written as before while the glow is off.

The glow is static, so nothing needs `InkWake`.

### Settings

Desk Look, "Ink and color":

- **Neon glow**, a switch, beside "Drop shadow under the text". Turning one on turns the other off.
- **Glow strength**, a slider, shown while the glow is on. Its accessible value is a percentage.
- While Neon glow is on, "Glow around the message" shows as on and is disabled, with the line "Covered
  by Neon glow." A `{noglow}` line in a message still wins.

The glow is stored with the ink (`desk_ink` gains `glow` and `glow_strength`), where the shadow already
lives. No new settings key, no new file.

### In the design format

One new top-level field, `glow`:

- `false`: off. `true`: on at the strength already set. A number from 0.2 to 1.0: on at that strength.
- A design that turns the glow on and sets `shadow: true` is rejected, naming both fields.
- `Merge`: turning the glow on turns the shadow off, and `shadow: true` turns the glow off.
- `Diff` and `Canonical` cover it. `get_desk` reports it. The `propose_desk` schema and its guide gain it.
- The chat-paste prompt teaches it (`SetupPrompt.DeskFields`).

`DeskDesign.cs` is already linked into the MCP server, so the trust boundary's allowlist does not change.
A 4.9 build would refuse a design carrying `glow`; a saved design only meets an older build after a
manual downgrade, which is not supported.

The Mac's MCP refuses `glow` by design, like the other fields the Mac does not draw. A note for the Mac
agent goes with the handover: the field's name and values, should the Mac ever draw it.

## 8. Code, by unit

| Unit | Where | What it does | Depends on |
|---|---|---|---|
| Look files | App, embedded | The six looks | the design format |
| `DeskLook`, `DeskLookCatalog` | Core, pure | Parses a look file, checks the must and must-not rules, lists the looks | `DeskDesign` |
| `DeskLookFit` | Core, pure | Fits a look to a wallpaper and fonts; says whether a design is an untouched look | the validator's contrast judgement |
| `glow` in `DeskDesign` | Core, pure | Parse, validate, merge, diff, canonical | none new |
| `DeskDesignService.ApplyLook` | App | Applies a look under rules A and B | `DeskLookFit`, `WriteDesign` |
| `DeskLooksViewModel`, the Looks section | App | The gallery in Settings | `ApplyLook`, the wallpaper crop |
| Setup's look row | App | Picks a look for Continue | `DeskLookCatalog` |
| The glow renderer | App, `DeskView` | The blurred copy, the padding, the under-icons path | the ink settings |

`DeskLookCatalog` and `DeskLookFit` are not linked into the MCP server. An agent does not list or apply
built-in looks in 5.0.

## 9. Errors

- **A look file that does not parse or breaks a rule** cannot ship: a test loads every embedded look. At
  run time a bad file is skipped and named in the app's log file by the design service
  (`LogSkippedLooks`), since debug output is dropped in a shipped build. The gallery shows the others.
- **The current Desk cannot be saved:** nothing is written; the status line says "The Desk could not be
  saved first, so nothing was changed."
- **The design cannot be written:** the status line says so with the exception's type, as `SwitchTo` does.
- **The wallpaper cannot be read:** the look is used as designed and the card falls back to a color.
- **The glow fails to draw on a piece** (an exception building the copy): that piece draws without it and
  the failure is written to debug output once, as the Desk's other drawing failures are. It does not reach
  the log file. A glow is never worth a missing piece.

## 10. Testing

**Core**

- Every look file (this test lives in the App tests, where the files are embedded): parses; passes
  `DeskDesign.Validate`; names every drawn piece; carries none of the must-not fields; names a fallback
  from the list of fonts present on build 17763.
- `DeskLookFit` over fixture wallpapers (dark, light, mid-grey, busy, and two monitors of opposite
  brightness): the result always passes the validator; the three ink steps each occur in some fixture;
  the notes match the step taken; an `auto` ink is never changed.
- The font step: installed, bundled, and missing.
- "Untouched look": a look at each of its fit steps counts; a moved piece, a changed size, a different
  custom ink, a different font each make it differ; a changed Launcher or message does not.
- `glow`: each value form; out-of-range numbers and wrong types refused; glow with `shadow: true` refused;
  merge both ways; diff lines; the canonical form.

**App**

- `ApplyLook` with a fake store: applying A then B then C saves one design; Put my Desk back returns to
  the first Desk, and a second press changes nothing; no built-in appears in the saved designs; a changed
  look is saved when replaced; the Desk is switched on when it was off; a failed save writes nothing.
- Every shipped look, applied and read back, is recognised as itself on every fixture wallpaper. The test
  lives in the App suite, and its round trip uses the same calls the App's `ApplyLook` makes.
- `SwitchTo` between two of the user's own designs still toggles with Put my Desk back.
- The Looks section: six buttons with their accessible names; focus moves without applying; the current
  look is marked; the status line is a live region; the jump link and the search words.
- Setup: the row is disabled while the tile is unticked; a pick applies only on Continue; no pick leaves
  setup as it was.
- The glow's settings: the two switches exclude each other; the message's switch shows as covered.
- The padding: 16 with the glow off; radius plus 4 at each end of the slider.

**MCP**

- `propose_desk` accepts each `glow` form and refuses glow with shadow; `get_desk` reports it; the schema
  test and `SetupPromptTests` follow the new field.

**Guard tests this touches:** `SettingsDeskSplitIndexTests` (a new first jump link),
`SettingsSourceScanTests`, `SettingsWindowNamesTests`, `StreamerReachTests` (the wallpaper crop reads a
path), `SetupPromptTests`. Not touched: `NetworkDisclosureTests` (no network), `ThirdPartyNoticesTests`
(no new font), `TrustBoundaryTests` (no new linked file), `ResetPlanTests` (no new file).

**By hand, on Neb**

- Each of the six looks on a dark and a light wallpaper, with "Desktop icons" on and off.
- The glow at both ends of the slider, on a gradient ink and a single color, with the plate on and off.
- Flip through all six, then Put my Desk back: the Desk from before returns, and Designs lists one new
  entry.
- Setup on a clean profile is not run on this PC (a dev build shares the installed app's data folders).
  It is covered by the App tests and by the release smoke on a fresh Store install.
- The Windows 10 font fallback is covered only by the unit test. There is no Windows 10 machine to try
  it on.

## 11. Data, privacy and the Store

- No new data, destination, capability or file. The looks are embedded; the wallpaper crop stays in
  memory; the glow is two values inside an existing setting.
- `docs/PRIVACY.md` and the hosted privacy page: no change.
- Reviewer letter: nothing required. What's New names the looks and the glow.
- The gallery and the cards pass the 10.1.4.4 checks: accessible names, keyboard reach, light and dark.

## 12. Not in this spec

- Wallpapers in looks (after 5.0, task `task-1795192163146-ab9ypxtf5`).
- Bundled fonts for looks, or a look that names a font to download.
- A pulsing glow, a glow per piece, or a glow color separate from the ink.
- Preview on hover. It can be added on top of the cards later.
- The user's own looks in the gallery, sharing looks, or a marketplace.
- An agent listing or applying built-in looks through the MCP.
- The Mac.
- The spectrum visualizer. It stays a stretch for 5.0 with its own decision, because it captures sound.
