# Rainmeter spike: what Knighthawk would import

Item H of the 3.8 widgets spec (`docs/widgets-3.8-spec.md` section 8). A report and a throwaway
analyser. No app code, nothing from the suite is copied here: the numbers and short key names
below are all the report quotes.

Recommendation, up front: **build tier 1 only for 3.9**, as a skill-driven import that ends in
the existing `propose_theme` and `propose_desk` review, and **do not build the tier 2
interpreter**. Tier 3 never. Details in the last section.

## Method

`tools/rainmeter-spike/analyse.py`, stdlib only, run as `python -I`. It opens every file read
only (no writes, no execution, Rainmeter never launched) and does this:

1. Parses every `.ini` and `.inc` in the Rainmeter dialect (UTF-8 or UTF-16, sections, `Key=Value`,
   `;` comments).
2. Resolves `@include` (`#@#` is `@Resources`; the including file's own options win, which is what
   the suite's own comments assume) and `#Var#` substitution to a fixed point. Anything left over
   is reported as unresolved.
3. Resolves `MeterStyle=A | B` inheritance, own options over the styles.
4. Finds each meter's bound measures (`MeasureName`, `[Measure]` references) and each measure's
   dependencies (`Url=[Other]`, bare names in a `Calc` formula).
5. Classifies, with taint:
   - Measures. Tier 2 allowlist: `Time`, `CPU`, `PhysicalMemory`, `SwapMemory`, `FreeDiskSpace`,
     `Calc`. Safe built-ins with no Desk piece yet (`NetIn`, `NetOut`) count as tier 2 and are
     listed as gaps. `Plugin`, `WebParser`, `Script` are tier 3. A `Calc` that depends, however
     indirectly, on a tier 3 measure is itself tier 3 ("tainted"). The report gives both the
     declared tier and the effective tier.
   - Meters. `String`, `Image`, `Bar`, `Roundline` are the tier 2 types (class `T2`). A `Shape`
     is not in that list, so by default it is tier 1 (look only). A `Shape` bound to safe
     measures whose geometry reads as a bar or an arc is reported separately as `T2i`
     (inferred). A meter bound to a tier 3 measure keeps its look (`T1`) and loses its data.
     Invisible `Shape` hit-boxes (alpha 1) are counted and dropped.
6. Scans every action-like option (`*Action`, the ActionTimer frame lines, `[Rainmeter]`
   section) for bangs and for bare program-launch commands.
7. Lists plugins, Lua, fonts (against the bundled files), skew matrices, colour and gradient
   variables, and the Desk piece each tier 2 item maps to, including the tier 3 items whose
   intent maps to a piece (weather, now playing, GPU).

Not in the suite, so not read: `Rainmeter.ini`, which holds each skin's window position, monitor
and z-order. See Risks.

## The suite

Six skins and one shared include, no Lua, no scripts:

| Files | Count |
|---|---|
| `.ini` skins | 6 (Clock, Date, System, TouchButton, Visualizer, Weather) |
| `.inc` | 1 (`@Resources/Variables.inc`, the theme) |
| `.ttf` | 3 (two handwriting faces in use, one retired) |
| `.png` | 1 (a 300 x 250 logo image, 6.3 MB) |
| backups (`.bak-*`) | 3 (ignored) |

About 1,500 lines of ini. Every skin pulls its theme from `Variables.inc`.

## Per-skin results

Meter classes: T1 look only, T2 allowed type with safe data, T2i a Shape inferred to be a bar or
ring. Measures: declared tier / effective tier after taint.

| Skin | Meters | T1 | T2 | T2i | Measures | Declared T2 / T3 | Effective T2 / T3 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Clock | 2 | 1 | 1 | 0 | 3 | 2 / 1 | 2 / 1 |
| Date | 4 | 2 | 2 | 0 | 3 | 3 / 0 | 3 / 0 |
| System | 20 | 13 | 4 | 3 | 14 | 7 / 7 | 5 / 9 |
| TouchButton | 2 | 1 | 1 | 0 | 0 | 0 / 0 | 0 / 0 |
| Visualizer | 30 | 28 | 2 | 0 | 70 | 40 / 30 | 4 / 66 |
| Weather | 8 | 6 | 2 | 0 | 10 | 0 / 10 | 0 / 10 |
| **Total** | **66** | **51** | **12** | **3** | **100** | **52 / 48** | **14 / 86** |

## Overall fractions

Meters (what you see), 66:

- 51 (77%) are look only. Inside that: 38 are bound to a tier 3 measure and would draw their
  resting state forever, 6 are invisible hit-boxes (one per skin, dropped), 7 are static art or
  text.
- 12 (18%) are tier 2 by type: 7 `String` bound to time or system measures, 5 `Image`.
- 3 (5%) are `Shape` bars that read as `slim` meters (the CPU, RAM and disk fills).
- 0 are tier 3 by type. Tier 3 shows up in the measures, not the meters.
- Live after a strict tier 2 importer: 12 of 66 (18%), or 15 of 66 (23%) with Shape inference.

Measures, 100:

- Declared: 52 tier 2, 48 tier 3 (52% / 48%).
- Effective, after taint: **14 tier 2, 86 tier 3 (14% / 86%)**. The gap is 38 `Calc` measures
  that feed the audio visualiser: each is a safe type that reads `AudioLevel`, so each is dead.
- Without the Visualizer: 30 measures, 10 tier 2 (33%), 20 tier 3 (67%).

What is in the tier 3 pile (86): 26 `AudioLevel`, 10 `WebParser`, 6 `UsageMonitor`, 4
`WindowsNowPlaying`, 1 `PingPlugin`, 1 `ActionTimer`, and the 38 tainted `Calc`. Lua: 0. Bangs:
387 are state changes (`SetVariable`, `UpdateMeter`, `Redraw`: almost all the 28-frame shimmer in
the clock), 2 are layout moves, 2 are fade effects, 1 is a `CommandMeasure` (tier 3), and 1
action value launches a program by path (tier 3). Plugins: 5 distinct, 38 uses.

Tier 1 inputs found:

- Fonts: 2 faces in 22 uses, both bundled in `@Resources/Fonts` (a bold marker face for headings,
  a thin pen face for body). A third face was retired from the theme on 2026-10-02 and is still on
  disk. Sizes run 10 to 80, mostly 14 to 18.
- Colours: three palette values (an electric azure glow, a medium blue ink, a near-black navy),
  a vertical text gradient and an upward bar gradient, a dim bar colour, a shimmer colour, and nine
  alias variables ("the bridge") that all point at those three.
- Skew: one matrix family only, shear 0.31, which is **17.2 degrees**. 36 meters slant one way
  (the left panels, up), 21 the other (the right panels, down). Inside the Desk's -20 to 20 range.
- Images: one PNG, 6.3 MB, over the 2 MB cap the pack image rules set (spec section 6).
- Positions: absolute `X`/`Y` per meter, relative `r` offsets for rows, right-aligned text
  anchored to a vertical "spine" at a fixed x.

## Desk mapping of the tier 2 items

| Rainmeter item | Count | Desk piece | Style |
|---|---:|---|---|
| `Time` with a clock format, and the `String` showing it | 1 + 1 | `clock` | upright |
| `Time` with a date format, and its `String`s | 3 + 2 | `clock` date line | upright |
| `CPU`, `PhysicalMemory`, `FreeDiskSpace` | 3 | `telemetry` | |
| their `String` labels (`Text=CPU \| %1%`) | 3 | `telemetry` label | `slim` |
| thin-bar `Shape` pairs bound to those measures (inferred) | 3 | `telemetry` bar | `slim` |
| `NetIn`, `NetOut` and their `String` | 2 + 1 | none yet (telemetry has no network) | |
| `Calc` that only combines safe inputs | 5 | follows its inputs | |
| `Image` without an action | 4 | sprite or own art (item C) | |
| `Image` with a program-launch action | 1 | `launcher` (the person picks the target) | |

By the #336 meter styles: every bar in this suite is a 4 px line with its label on the row above,
which is exactly `slim`. No `Roundline`, `Bar` or `Histogram` meter appears anywhere, so `ring`
and `ticks` get no input from Knighthawk. Rainmeter's `Roundline` would map to `ring`, and `Bar`
to `bar`, `slim` or `ticks` by size, but this suite is the wrong sample to prove it.

Tier 3 items whose **intent** maps to a Desk piece (rebuilt there, not imported):

| Rainmeter item | Count | Desk piece |
|---|---:|---|
| `WebParser` against a public forecast API, with its condition-code table | 10 measures, 4 strings | `weather` |
| `WindowsNowPlaying` (named through a variable) | 4 measures, 3 strings | `nowPlaying` |
| `UsageMonitor` for GPU, disk throughput, top process | 6 | `telemetry`, if it grows a GPU field |
| `AudioLevel` visualiser | 26 + 38 `Calc` | none |
| `PingPlugin`, `ActionTimer` (the clock shimmer) | 2 | none |

Counting those intent hints, 22 of 66 meters (33%) carry something live on the Desk.

## What a tier 1 and 2 importer would produce for Knighthawk

Theme. Ink 60,110,210; accent and glow 0,80,255; base 0,10,40; a 180 degree text gradient from
the ink to the base (stops at 0.1 and 0.9); an upward bar gradient from the base to the ink; a
shimmer highlight. These go through `propose_theme` as a theme plus an ink gradient. The two
fonts come across as the person's own fonts only when the files are the person's to use (see the
license note).

Layout, from the geometry. Knighthawk is a **fold**: a left half that slants up toward a
vertical spine and a right half that slants down away from it. The Desk allows one slant per
stack, so the importer produces two stacks rather than one:

- A left stack tilted about -17 degrees (the `StyleTextLeft` and bar rows: CPU, GPU, RAM, disk,
  network), right-aligned text, `slim` meters, all in the glow ink. GPU arrives as a labelled
  zero until `telemetry` has GPU.
- A right stack tilted about +17 degrees (top process, disk throughput, ping), the same ink and
  font, left-aligned. Those three are `UsageMonitor` and `PingPlugin` readings, so as imported
  they are static labels. The honest output is to leave them off and say why.
- The clock: right-anchored, 80 pt marker face, -17 degrees, glow shadow, the shimmer as the
  existing clock effect if one fits and otherwise dropped. The date rides the same stack as a
  date line: "weekday, day | month" on the up-slant, the year on the down-slant, with the corner
  post between them.
- The weather piece in the same fold: city, condition and temperature, then today's and
  tomorrow's high and low. The Desk supplies the data; the import supplies only the look.
- The now-playing text (artist, title, time) as the `nowPlaying` piece with the suite's ink.
  The 24-bar visualiser itself is dropped: nothing on the Desk draws system audio, and 64 of its
  70 measures are tier 3 or tainted.
- The logo: the one `Image` becomes a `launcher` tile with its brightness change on hover, after
  a downscale (6.3 MB is over the 2 MB cap), and **without its target**: the action is a
  hard-coded Chrome app path, which is a bare program launch. The person picks what it opens.

Honestly lost: the invisible hit-boxes (fine), the 2 px glowing spine (no Desk primitive), the
per-glyph gradient and inline shadows on text (the Desk has an ink gradient and glow per piece,
not per run), the 24 reactive bars, GPU and process readouts, the shimmer animation.

Two things the importer should surface as warnings, both real in this suite: the TouchButton
skin includes `Variables.ini` where the file is `Variables.inc`, so Rainmeter itself leaves its
glow colour undefined (the analyser reports one unresolved variable); and the theme file carries
a latitude/longitude pair, an absolute pixel anchor and a GPU index, which are the home's
numbers, not a look. They must not leave the machine with an exported pack.

## License and author

Every skin sets the `Author` key in `[Metadata]` to the same single handle, the suite owner's own.
No skin sets `License`, `Information` or `Version`. There is no license file. The fonts in
`@Resources` are the owner's own hand-drawn faces, and the PNG and layouts are the owner's work,
so this suite carries no third-party terms that the analyser can see. The retired third face
(`gravity-light`) is not mine to vouch for and is not used.

That is not true of most skins in the wild, where Rainmeter's convention is `License=Creative
Commons Attribution-NonCommercial-ShareAlike 3.0` and an `Author` line. An importer has to:

- Copy `Name`, `Author`, `License`, `Information` and `Version` verbatim into the pack manifest it
  writes, and show them in the Apply review.
- Treat any `NonCommercial` or `NoDerivatives` license as local-only. The pack rules already make
  user art local-only; an imported pack must never be offered for the signed catalog, and the
  Store build must not ship one inside the app.
- Not copy fonts or images it cannot attribute. A font file carries its own license; the
  importer should import a font only when the person confirms it is theirs.
- Keep the attribution when the pack is edited afterwards.

## Risks

- **A sample of one.** Knighthawk is hand-built, Shape and String heavy, and has no `Bar` or
  `Roundline` at all. The tier 2 list the task defines would have touched 12 of its 66 meters.
  Other suites will differ, and this one cannot say by how much.
- **Placement lives outside the suite.** Window position, monitor, z-order and click-through are in
  `Rainmeter.ini`, not in the skin files. Without it, the importer can only produce relative
  layout (stack, anchor, tilt), which is what the Desk uses anyway, but it cannot reproduce "this
  sits at x=1186 on monitor 2".
- **Dialect depth.** Section variables, `DynamicVariables`, `IfCondition`, `Group=`, `[#Var]`,
  nested `Calc` functions, and UTF-16 files are all legal. The analyser handles the subset this
  suite uses. Production parsing needs a corpus, fuzzing and hard caps (file count, depth, size).
- **Path safety.** `@include`, `ImageName` and font paths can point anywhere. Resolution must stay
  inside the chosen suite folder and reject `..` and absolute paths.
- **Privacy.** Skins hold coordinates, drive letters, user paths and URLs. Nothing from an import
  is uploaded; the review lists what was dropped on purpose.
- **Look fidelity.** The Desk has one slant per stack, no per-run gradients and no Shape
  primitives. Many skins will import as "the colours and fonts, roughly". Promising more invites
  complaints.
- **Trademark and the Store.** Using the word Rainmeter needs the same disclaimer treatment as
  Claude under the 10.1.4.4 tests ("not affiliated with"), and a user-facing "import" feature
  that reads arbitrary folders is a certification surface.
- **Upkeep.** Tier 2 is a second renderer for Rainmeter semantics inside a Desk that already has
  its own. Every Rainmeter edge case becomes a bug in Sanduhr.

## Recommendation for 3.9

**Build tier 1 only. Do not build tier 2. Tier 3 never.**

- Tier 1 pays for itself on this suite: theme (palette, two gradients, shimmer), fonts, the
  slant, the fold as two stacks, the logo as a launcher tile. That is the part of Knighthawk a
  person sees as "my skin", and it needs no interpreter, no code and no new Desk primitive. The
  Desk already has the review-and-apply path (`propose_theme`, `propose_desk`), the tilt (#314)
  and `slim` (#336).
- Do it as the `desk-setup` skill's job (item I), not as a parser in the app: the analyser's
  parsing plus Claude's judgment proposes the theme and the layout, the person reviews and
  applies, and the dialect can be fixed daily without a release. Promote to an in-app "Import a
  Rainmeter skin" only if people ask for it by name.
- Add the **intent hints** to tier 1: a small table that turns `WebParser` against a forecast
  API into "use the weather piece", `WindowsNowPlaying` into `nowPlaying`, `UsageMonitor` GPU
  into a telemetry wish. It raises Knighthawk from 15 live meters to 22 of 66 and costs a lookup
  table, not a renderer.
- Skip tier 2. A strict importer makes 18% of the meters live (23% with Shape inference) and
  14% of the measures, and 38 of the 52 declared-safe measures turn out tainted anyway. The tier 2
  types it targets (`Bar`, `Roundline`) do not appear in the suite it is meant to serve. The
  two Desk gaps it would expose (network telemetry, GPU) are cheaper to add as Desk features.
- Before any of this ships, collect three or four public skins under CC licenses, run the
  analyser on them, and see whether `Roundline` and `Bar` show up. That is the evidence that
  would reopen tier 2.
- Whatever ships keeps the license rules above: metadata preserved, `NC` and `ND` local-only,
  fonts only on the person's say-so.

## Reproducing

```
python -I tools/rainmeter-spike/analyse.py "<path to the skin suite>"
python -I tools/rainmeter-spike/analyse.py "<path>" --json
```

With no argument it looks for `Knighthawk` under `Documents\Rainmeter\Skins` and its OneDrive
equivalent. Output is counts and short key names only.
