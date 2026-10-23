# Spec: Sanduhr 3.9 (Windows), CI watchers from local runners

Status: draft for review, 2026-10-23. Headline of 3.9, set by the owner the same day (626 dashboard
decisions llLZsYQvtvRTCy1cGOvh and its correction qR4gluQtgqnRLRHyZNOF). It is item D of
`docs/widgets-3.8-spec.md`, held from 3.7 and deferred from 3.8 for want of real job lines; the
lines are now captured (section 7). Dashboard task: "CI watchers from local runners (3.9 headline)".

## 1. What ships

A watcher card on the Desk for every job a GitHub Actions runner **on this PC** runs, however the
job was started (a push from Dunder, a PR, a re-run on github.com). "Running job: test" puts a card
up; "Job test completed with result: Failed" ends it red and glows. Sanduhr reads the runner
containers' own log output through Docker's local pipe: no GitHub token, no account, no network.

Not this feature: Claude Code putting up a card for a CI run it is watching. That already ships
(`watch_start` / `watch_update` / `watch_end`, "Watchers from Claude" in PRIVACY) and is untouched.

## 2. The rule that does not move: list and logs, never inspect

A runner container's environment holds its registration token (`RUNNER_TOKEN` in Neb's
`ci-runners/compose.yml`). Docker's inspect call (`GET /containers/{id}/json`) returns that
environment. Sanduhr therefore makes exactly two Docker calls and no others:

| Call | Used for |
|---|---|
| `GET /containers/json` | Which runner containers exist and are running: id, image, names, state, and the `com.docker.compose.service` label. Verified 2026-10-23 on Neb: the response carries no `Env` and no token. |
| `GET /containers/{id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200` | The runner's own output, followed while the container runs. |

`DockerPipe` builds requests only from a two-value enum; there is no way to pass a path. Sanduhr
never writes to Docker and never starts, stops or execs anything.

As built, `DockerPipeBoundaryTests` pins the boundary by structure, not by a list of forbidden
words. The only `HttpClient` call is `GetAsync(PathFor(...))`. A scan of
`src/Sanduhr.App`, `Sanduhr.Core` and `Sanduhr.Mcp` (each directory must exist) forbids the pipe
name and Docker port tokens outside `src/Sanduhr.App/Desk/DockerPipe.cs`, so the pipe name appears
only in `DockerPipe.cs` and no other file can reach Docker at all. The container id in the logs
path must match `^[0-9a-f]{64}\z`, or the call is refused before it is built. Redirects are off.
The logs call has a 10 second limit on its headers; the followed body has none.

## 3. Which containers count

A container is a runner when its image name, without registry, owner or tag, contains
`actions-runner`, `github-runner` or `gh-runner`. That covers Neb and Dunder's
`dunder-gh-runner`, GitHub's `ghcr.io/actions/actions-runner`, `myoung34/github-runner` and
`summerwind/actions-runner`. The tag is ignored on purpose: the runner updates itself past its
image tag (Neb's `delta-1` runs 2.338.0 on a 2.337.0 image). Everything else Docker runs (the
egress proxy, databases, anything) is never read. Decision R32ePkdyeuC4w0jfI6pW.

The runner's name on a card is the Compose service label (`alpha-1`), else the container's
first name without its leading `/`.

## 4. The pieces

**Core, pure, tested on fixtures:**

- `RunnerLog.Parse(string line)` returns `JobStarted(job, at)`, `JobEnded(job, result, at)`,
  `RunnerOnline(at)` or nothing. A line is `<docker RFC 3339 stamp> <runner stamp "yyyy-MM-dd HH:mm:ssZ">: <text>`;
  the runner's own stamp is the job's time. Texts:
  - `Running job: <name>` starts.
  - `Job <name> completed with result: <word>` ends. `Succeeded` and `SucceededWithIssues` are
    passed; `Failed` and `Abandoned` are failed; `Canceled` and `Skipped` are finished (ended, no
    result to celebrate or mourn). An unknown word is finished.
  - `Listening for Jobs` is `RunnerOnline`, and only that line.
  - Everything else, including `Runner reconnected.` (it can come mid-job after a network blip),
    `Runner connect error`, `A session for this runner already exists`,
    `√ Connected to GitHub` and blank lines, is nothing.
  - The job name goes through `WatcherLimits.Line(_, Title)`.
- `DockerLogFrames`: Docker's multiplexed log stream (the runners have no TTY) in 8-byte-header
  frames, decoded into whole lines. HTTP chunked encoding is `HttpClient`'s: its connection is the
  pipe, through `SocketsHttpHandler.ConnectCallback`, and the class decodes frames only. Frames and
  lines split across reads are joined; a frame over 64 KB is skipped.
- `WatcherSource.Runner` and board operations `RunnerStart(runner, job, at)`,
  `RunnerEnd(runner, job, state, at)` and `RunnerGone(runner)`. Id `r:<runner>:<job>`. A runner
  runs one job at a time, so a new start on the same runner ends any open card of that runner as
  finished first. The operations take plain values (strings, a state, a time), because
  `Watchers.cs` is source-linked into the MCP server and its allowlist stays pure Core. The
  `ApplyRunner` extension, which feeds them from parsed `RunnerLog` results, lives in
  `RunnerBoard.cs`, outside the link.

**App:**

- `DockerPipe`: `\\.\pipe\docker_engine` (Docker's default context; verified on Neb with Docker
  Desktop on the Linux engine, where it answered the list call), HTTP/1.1 over a
  `NamedPipeClientStream`, API version pinned in the path (`/v1.43/`, verified). The two calls in
  section 2 and nothing else.
- `RunnerWatchService`: on only while the setting is on. Lists containers every 30 seconds;
  follows each running runner's log on its own task; feeds parsed lines to the board on the UI
  thread. A runner that leaves the list (stopped, removed) is `RunnerGone`.

## 5. Behavior

- **Off by default.** Settings ▸ Desk ▸ Watchers: **Show my CI runners' jobs**. Under it, one
  status line: "Watching 6 runners: bravo-1, alpha-1, …" (names clipped to fit), "No
  runner containers are running.", "Docker isn't running.", "Docker answered but refused the
  request." (an HTTP error status), or "Docker refused the connection. Your Windows account needs
  to be in the docker-users group."
- **Cards.** Title `<runner> · <job>` ("alpha-1 · test"), short title the job name, note
  "CI runner", no link (the runner's lines carry no run URL), elapsed from the runner's own stamp.
  States and their looks are the board's: running, then passed (fades after 6 s), failed (stays
  until dismissed, glows once) or finished (fades).
- **One card per container.** As built, the board tells runners apart by the container's own name
  (its first name, unique per daemon and stable across a Compose recreate), not by the service label,
  which scaled replicas share. The label stays on the card, in the spoken text and in the status
  line; replicas that share one show their container names instead.
- **No lost touch.** A runner prints nothing while a job runs, so the agent's 10-minute window
  would grey every long build. `WatcherBoard.Window(Runner)` is unbounded; a runner card ends only
  on its result line, a new job on the same runner, a `RunnerOnline` line after it started (the
  runner restarted mid-job), or `RunnerGone`, the last three as finished.
- **Starting mid-job.** The follow begins with the last 200 lines. Lines stamped more than 30
  seconds before the follow opened are replay: they rebuild each runner's open job but put up no
  card for a job that already ended, so starting Sanduhr does not flash old green cards. A job
  still open at the end of the replay gets its card with its real start time. When the replay ends
  with no open job, the runner is idle (`RunnerFollow.EndReplay` returns `Online`), so a card left
  from before the follow ends as finished.
- **Docker going down** ends every open runner card as finished. Sink calls from a follow queue in
  order on the UI thread, and are dropped once that follow is cancelled, so a late line never
  revives a card after the switch goes off or a runner leaves.
- **Docker down or gone.** Retry the list every 60 seconds, quietly; no toast, no error card, no
  log line per retry (one log line when the state changes). Turning the setting off closes every
  follow and clears runner cards.
- **Demo mode hides runner cards.** Service names can name private repos. The failure glow is
  skipped in demo mode too.
- **This PC only.** A job on Dunder's runners shows on Dunder's Desk.
- **The companion** reacts with `job_passed` to a runner card that passes; no new code
  (`CompanionMood` already fires on any watcher that ends passed).
- **Screen readers** read a runner card as "CI job test on alpha-1, running, 3m", the existing
  spoken-watcher shape;
  the status line under the switch is a live region.

## 6. Store, privacy, settings

- No new capability: a named-pipe client works under `runFullTrust`. Nothing written to
  `%USERPROFILE%\.sanduhr`, nothing new on disk: one boolean in `settings.json`
  (`watchers_ci_runners`), cards in memory only, as with every watcher.
- **No network.** Docker's pipe is local IPC. The reviewer letter for 3.9.0.0 says so in one line:
  "Optional, off by default: reads the log output of GitHub Actions runner containers on the
  user's own PC through Docker's local pipe, to show job progress. No network, no credentials."
- **PRIVACY**, a new row beside "Watchers from Claude": what is read (runner containers' log
  lines, matched by image name; the container list's names, image and state), what is kept (job
  name, runner name, start and end time, result, in memory until its card fades, the user dismisses
  it (a failed card stays until then), the switch goes off or Sanduhr quits; the runners' names
  too, for the status line; the list is read every 30 seconds, every 60 while Docker is down or
  refusing), what is never read (any other container, any container's environment or settings, files
  inside containers), nothing sent anywhere, nothing about jobs or runners logged (the app logs
only through `Debug.WriteLine`, which release builds compile out). The hosted privacy
  page follows (hub issue, the other machine).
- **Reset Sanduhr…** needs nothing new beyond the setting.

## 7. Fixtures

Real, captured on Neb with runner names generalized (`ci-runners-alpha-1-1`, run of 2026-10-22), Docker stamps kept:

```
2026-10-22T01:59:10.762174669Z 2026-10-22 01:59:10Z: Listening for Jobs
2026-10-22T02:00:03.736011454Z 2026-10-22 02:00:03Z: Running job: compile
2026-10-22T02:00:18.054757098Z 2026-10-22 02:00:18Z: Job compile completed with result: Succeeded
2026-10-22T02:00:19.799051744Z 2026-10-22 02:00:19Z: Running job: test
2026-10-22T02:01:58.598577583Z 2026-10-22 02:01:58Z: Job test completed with result: Succeeded
```

Real reconnect noise, runner names generalized (`ci-runners-bravo-1-1`, 2026-10-23, blank lines dropped), which must
parse to nothing except `Listening for Jobs` (`RunnerOnline`), `Runner reconnected.` included:

```
2026-10-23T18:54:52.723113448Z A session for this runner already exists.
2026-10-23T18:54:52.723263041Z 2026-10-23 18:54:52Z: Runner connect error: Error: Conflict. Retrying until reconnected.
2026-10-23T18:55:23.830324801Z √ Connected to GitHub
2026-10-23T18:55:55.875739249Z 2026-10-23 18:55:55Z: Runner reconnected.
2026-10-23T18:55:55.876180740Z Current runner version: '2.337.0'
2026-10-23T18:55:55.876189246Z 2026-10-23 18:55:55Z: Listening for Jobs
```

**Derived** (same line shape, result word swapped; marked `derived` in the fixture file, decision
FdHYu1NknZneGkE71adp): `Failed`, `SucceededWithIssues`, `Skipped`, `Abandoned`, a job
name with spaces and parentheses (`test (ubuntu-latest, 3.12)`), and a restart mid-job (a
`Running job` followed by `Listening for Jobs` with no result). Swap a derived line for a real one
when a Neb or Dunder job produces it.
`Canceled` is now real: Neb's runner printed it on 2026-10-23 (`neb-alpha-canceled.log`).

The same lines are mirrored into the runner's `_diag/Runner_*.log` as
`[<stamp> INFO Terminal] WRITE LINE: <line>`, the route for runners that are Windows services
rather than containers. That route is not in 3.9.

## 8. Tests

- Core: `RunnerLog` on every fixture line; `DockerLogFrames` on frames split at every byte offset,
  two frames in one read, a frame over the cap, chunked encoding with extensions; the board's
  runner operations (no lost touch at 1 h or 24 h, new start ends the open card, `RunnerOnline`
  after a start ends it, replay rule, demo mode hides).
- The trust test of section 2.
- App: `RunnerWatchService` against a fake pipe (Docker down, access denied, a runner appearing
  and leaving, setting off clears); Settings names and the live region (the a11y tests that pin
  every Settings control's name).
- Smoke on Neb: the status line lists the six runners; a real job on a Neb runner puts up and ends
  a card. That needs one job routed to Neb (`CI_RUNNER=neb` on a repo, then removed), with the owner's
  nod at verify time, or the check runs on Dunder, whose runners take jobs by default. As built,
  the debug-build live check and the real-job smoke are done with the owner, not by an agent: a dev
  build must not run beside the live release app.

## 9. Order of work

1. Fixture file and `RunnerLog` (Core), with tests.
2. `DockerLogFrames` (Core), with tests.
3. The board's runner source (Core), with tests.
4. `DockerPipe` and the trust test (App).
5. `RunnerWatchService`, the setting, the status line, demo-mode hiding (App).
6. PRIVACY row, reviewer letter line, CHANGELOG, a What's New card (`WhatsNewTests` pins the
   table), version lockstep for 3.9.0 (`docs/release-runbook.md`).

## 10. Later, not 3.9

- Runners installed as Windows services, through `_diag/Runner_*.log`.
- The Mac: the same Core logic over `/var/run/docker.sock`, as its own Mac item.
- A link to the run. The runner's lines carry none; adding one would need GitHub, which this
  feature exists to avoid.
