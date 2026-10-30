<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: desk-2-38

## D1
- **Decision:** The system piece's layout word is `telemetry`, as on Windows; the UI calls it "System".
- **Why:** Windows' `DeskArrangement.Pieces` and `propose_desk` use `telemetry`, and the Mac's `DeskDesign` already maps `telemetry` to "the Windows system piece". One word keeps layouts, designs and the MCP shared.
- **Alternative not taken:** `system` as the spec's prose names it, which would need an alias on both sides.
- **Item:** 1, 3
- **How to reverse:** Rename the layout word in `DeskArrangement.swift` and the validator, and accept `system` as an alias in reading.

## D2
- **Decision:** The bars item is a written spike, held, with no tap code.
- **Why:** Settled call 6 says spike first and ship only if the owner accepts the System Audio Recording prompt. A permissions surface is `hold` by default, and running a probe would put a TCC prompt on the screen with nobody there.
- **Alternative not taken:** Building the tap behind an off switch now.
- **Item:** 4
- **How to reverse:** After the owner's call, plan the tap and `AudioBarMotion` port as its own item.

## D3
- **Decision:** The permission preflight passed on the session's own auto mode.
- **Why:** The session runs under the auto-mode classifier (it refused a PR merge earlier tonight), so tool calls do not wait on prompts. Merges and pushes are out of the run anyway.
- **Alternative not taken:** Asking the owner to switch modes.
- **Item:** run
- **How to reverse:** n/a

## D4
- **Decision:** System memory reads active + wired + compressed pages against physical memory.
- **Why:** Closest Mac match to Windows' memory load (`MEMORYSTATUSEX.dwMemoryLoad`). It runs a little above Activity Monitor's "Memory Used".
- **Alternative not taken:** Activity Monitor's app-memory formula (no public API gives it exactly).
- **Item:** 1
- **How to reverse:** Change the sum in `TelemetryReader` and its test.

## D5
- **Decision:** Until item 3, `DeskDesign` checks its Windows-only refusals before the known-piece list, so `telemetry` from a design is still refused with the "arrives in 2.38.0" reason.
- **Why:** Item 1 made `telemetry` a known Desk piece; without the guard a design could place it before the validator and the Python mirror agree.
- **Alternative not taken:** Accepting it in item 1 and leaving Python behind.
- **Item:** 1 (removed in 3)
- **How to reverse:** Item 3 removes the guard.

## D6
- **Decision:** `DeskModel` (not main-actor) owns a `WeatherService` (main-actor) through a `nonisolated` init and `MainActor.assumeIsolated` at each use.
- **Why:** Every caller is on main: the defaults observer posts on `.main`, `DeskController` and `DebugHooks` are `@MainActor`, Settings runs on main. Making `DeskModel` main-actor would ripple through every Desk file.
- **Alternative not taken:** Annotating `DeskModel` `@MainActor`.
- **Item:** 3
- **How to reverse:** Mark `DeskModel` `@MainActor` and drop the `assumeIsolated` calls; a new off-main caller would crash until then.

## D7
- **Decision:** A design's `system` piece gets the generic "is not a Desk piece" refusal, not a pointer to `telemetry`.
- **Why:** Windows has no `system` word and gives the generic message; Windows wins on rules and messages.
- **Alternative not taken:** A Mac-only hint naming `telemetry`.
- **Item:** 3
- **How to reverse:** Add a reason for `system` in `DeskDesign.swift` and the Python mirror, with a fixture.

## D8
- **Decision:** The weather piece's Open-Meteo credit is plain text, not a link.
- **Why:** Desk pieces sit under the click layer; a clickable credit needs a hit area of its own. The Settings ▸ Weather page carries the clickable CC BY 4.0 link.
- **Alternative not taken:** Porting Windows' clickable credit.
- **Item:** 3
- **How to reverse:** Add a hit area for the credit in `DeskClickLayer`.

## D9
- **Decision:** Runner watcher cards reach the band file as `source: automatic, kind: runner, state` only, with no title.
- **Why:** Adding the `.runner` source made `BandFile.entry()` non-exhaustive. A runner's name or job can name a private repo, so only the state crosses into Claude Code sessions. Windows has no band file to follow.
- **Alternative not taken:** Leaving runner cards out of the band, or sending the job title.
- **Item:** 5 (PRIVACY line in 7)
- **How to reverse:** Return nil for `.runner` in `BandFile.entry()`.

## D10
- **Decision:** Runner fixtures were renamed from a machine name to `host-*`, and the "docker-users group" denial text became Mac wording.
- **Why:** No machine names in the public repo; the docker-users group is Windows-only.
- **Alternative not taken:** Copying Windows' files and text verbatim.
- **Item:** 5
- **How to reverse:** n/a
