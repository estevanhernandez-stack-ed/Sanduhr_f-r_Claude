# Run List: desk-2-38

- [x] **1. The system piece**
  Spec ref: `docs/mac-widgets-spec.md > 12`; Windows `Telemetry.cs`, `DeskSensorsNative.cs`, `DeskArrangement.cs`
  What to build: A pure `Telemetry` model ported from Windows (CPU percent from tick deltas, memory used against physical, disk used percent, the rows it shows), a sampler using `host_processor_info` tick deltas, `host_statistics64` and the boot volume's `volumeAvailableCapacityForImportantUsage`, every 5 s and only while the Desk shows (no timer when hidden). The Desk piece `telemetry` (named "System" in the UI) joins the layout words with its default anchor and the meter styles (`bar|slim|ring|ticks`) as on Windows; it renders with the existing meter-style views. Settings ▸ Desk gets its place and style rows (SettingsIndex). Demo mode shows made-up readings. Read locally, never sent or stored. Idle CPU rules hold. Size L.
  Acceptance: Done when the ported Telemetry tests and layout-word tests pass, the sampler stops when the Desk hides, and the suite passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [x] **2. Weather: lookup, forecast and settings**
  Spec ref: `docs/mac-widgets-spec.md > 13`; Windows `Weather.cs`, `WeatherService.cs`, `WeatherTests.cs`
  What to build: Port `Weather.cs` (URL building, geocoding and forecast parsing, codes to words and symbols, staleness at 3 h) and a `WeatherService`: Look up sends only the typed name to `geocoding-api.open-meteo.com`; the chosen place's latitude and longitude go to `api.open-meteo.com` every 30 minutes, only while the piece is placed; user agent `Sanduhr`; no Location Services, no key. Settings ▸ Weather (city field, Look up, results, chosen place, "Show it as" nickname up to 24 characters, written only when set). Tests stub URLSession (URLProtocol), never the network. Size L.
  Acceptance: Done when the ported Weather tests pass, a test proves the nickname never reaches either request, and the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [x] **3. The weather piece and propose_desk/get_desk**
  Spec ref: `docs/mac-widgets-spec.md > 13, 8`
  What to build: The Desk `weather` piece (default anchor as Windows, nickname shown instead of the place name, stale state). `DeskDesign` stops refusing `telemetry`, `weather` and `weather {nickname}`: Swift validator and apply, the Python mirror in `mac/integrations/sanduhr_mcp.py` (guide text from Windows' `ToolCatalog.cs`), `get_desk`/`desk-state.json` report the nickname and the pieces; shared fixtures under `mac/Tests/SanduhrTests/Fixtures/desk-designs/` updated so both sides agree. Size M.
  Acceptance: Done when Swift and pytest pass with the updated fixtures, `""` nickname goes back to the place's name, and over-24 is refused.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [~] **4. Bars spike: the Core Audio process tap**
  Spec ref: `docs/mac-widgets-spec.md > 14`, settled call 6
  What to build: A written spike only, `docs/spikes/2026-10-30-audio-tap-bars.md`: what `AudioHardwareCreateProcessTap` (macOS 14.2+) needs (TCC System Audio Recording, `NSAudioCaptureUsageDescription`), how the prompt reads, whether a per-process peak can be had without the permission, what the switch and `AudioBarMotion` port would cost, and a recommendation. From Apple's headers in the installed SDK and documentation; no code that requests the permission, no prompt triggered. Permissions surface, so held for the owner's call.
  Acceptance: Done when the write-up answers the two questions settled call 6 asks (does the tap need the permission, how does its prompt read) with sources.
  Verify: `test -s docs/spikes/2026-10-30-audio-tap-bars.md`
  Stop class: hold

- [x] **5. Runner log rules (pure ports)**
  Spec ref: `docs/mac-widgets-spec.md > 15`; Windows `DockerContainers.cs`, `DockerLogFrames.cs`, `RunnerLog.cs`, `RunnerBoard.cs`, `RunnerFollow.cs`
  What to build: Swift ports of which containers count, the multiplexed log frame decoder, the log grammar (`Running job:`, `Job … completed with result:`, `Listening for Jobs`), the board and follow rules, with Windows' tests and captured fixtures copied into `mac/Tests/SanduhrTests/Fixtures/runners/`. Pure. Size M.
  Acceptance: Done when the ported tests pass on the copied fixtures.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [x] **6. CI watchers: the Docker socket and the cards**
  Spec ref: `docs/mac-widgets-spec.md > 15`; Windows `DockerPipe.cs`, `RunnerWatchService.cs`, `DockerPipeBoundaryTests.cs`, `CiRunnersSwitchTests.cs`
  Depends on: 5
  What to build: A small HTTP/1.1 client over the Unix socket (`~/.docker/run/docker.sock`, then `/var/run/docker.sock`) with `NWConnection`, built from a two-value request enum (`GET /containers/json`, `GET /containers/{id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200`, id must match `^[0-9a-f]{64}$`), no redirects, 10 s header limit, never inspect/write/start/stop/exec. A runner watch service feeding cards into the existing watchers piece so the companion's triggers see them. Off by default behind Windows' switch name, in Settings. A boundary test: the socket path appears in one file only, and that file's only request call takes the enum. Size L.
  Acceptance: Done when the boundary and switch tests pass, the service is tested against a fake transport, and the suite passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [x] **7. Docs, the 2.38.0 What's New cards, CHANGELOG**
  Spec ref: `docs/mac-widgets-spec.md > 16` (2.38.0 rows)
  Depends on: 1, 2, 3, 6
  What to build: PRIVACY Mac section (system readings local only; the two Open-Meteo hosts and exactly what each sees; the Docker socket and its two requests; no audio tap), README sections, `docs/mac-smoke-test.md` sections, What's New 2.38.0 cards with their `WhatsNewTests` lines, CHANGELOG `## Unreleased (mac)` after `# Changelog`. Every claim true to the code. No Info.plist. Size S.
  Acceptance: Done when WhatsNew tests pass and docs describe only what items 1-3, 5 and 6 built.
  Verify: `cd mac && ./test.sh`
  Stop class: go
