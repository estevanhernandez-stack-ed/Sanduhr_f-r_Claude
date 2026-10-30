# Run Brief: desk-2-38

**Asked (verbatim):** "do it", after the proposal to run 2.38.0 overnight with `/take` (sections 12 to 15
and the 2.38.0 rows of 16, bars as a spike that parks the owner call).

**Source:** `docs/mac-widgets-spec.md`, sections 12 (system piece), 13 (weather piece and nickname), 14
(livelier now-playing bars), 15 (CI watchers from local runners) and the 2.38.0 rows of section 16.
Windows reference, read only: `windows-dotnet/src/Sanduhr.Core/{Telemetry,Weather,AudioBars,DockerContainers,
DockerLogFrames,RunnerBoard,RunnerFollow,RunnerLog}.cs`, `src/Sanduhr.App/Desk/{WeatherService,DockerPipe,
RunnerWatchService,DeskSources}.cs`, `src/Sanduhr.App/Interop/DeskSensorsNative.cs`, and their tests.

**Intent:** The Mac Desk gains the two pieces Windows has that it lacks (system readings, weather with its
nickname), and the watchers piece gains cards for local GitHub Actions runner jobs. The bars question gets a
written spike so the owner can decide on the System Audio Recording prompt.

**Done when:** Items 1 to 3 and 5 to 7 are merged on `cart/desk-2-38`, item 4 is held with its spike write-up,
and `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there.
The owner then runs the live checks (system piece, weather lookup, runner cards), and the 2.38.0 ship commit
comes after.

**Out of scope:** `mac/Info.plist`, the release, any push or PR, anything under `windows-dotnet/`, building the
audio tap or any code that asks for System Audio Recording, 2.39.0 (own sprites), any live network request in
tests (Open-Meteo and the Docker socket are stubbed).

**Inferences:**
- The layout word for the system piece is `telemetry`, as on Windows (`DeskArrangement.Pieces`) and in
  `propose_desk`, so layouts and designs stay shared; it is named "System" in the UI (ledger D1).
- Windows' tests are the case lists; the spec wins on Mac surfaces, Windows on rules and messages.
- `propose_desk`/`get_desk` stop refusing `telemetry`, `weather` and `weather {nickname}` on the Mac; the
  Python validator and the shared fixtures move with the Swift one.
- What's New cards are added with version "2.38.0" before Info.plist moves.

**Mode:** unattended. The ask followed an offer to run overnight at 00:43; auto mode is on (the session's
permission classifier is active).
