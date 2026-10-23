# CI Watchers from Local Runners Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a Desk watcher card up for every job a GitHub Actions runner container on this PC runs, read from the containers' log output through Docker's local pipe, with no token and no network.

**Architecture:** Pure Core units turn Docker's bytes into board changes: `DockerLogFrames` (multiplexed frames to lines), `RunnerLog` (a line to a job start, end or runner restart), `RunnerFollow` (holds back the replayed tail), `DockerContainers` (the list call's JSON to runner containers) and a new `WatcherSource.Runner` on the existing `WatcherBoard`. The App adds `DockerPipe` (an `HttpClient` whose connection is the `docker_engine` named pipe, able to make exactly two calls) and `RunnerWatchService` (lists every 30 s, follows each runner's log, applies lines through the existing `WatcherStore`). A setting, a status line and the docs finish it.

**Tech Stack:** .NET 10, WPF, xUnit, `System.IO.Pipes`, `SocketsHttpHandler.ConnectCallback`, CommunityToolkit.Mvvm (existing view models).

**Spec:** `docs/ci-watchers-3.9-spec.md` (approved 2026-10-23). Read it before starting.

## Global Constraints

- Docker calls: `GET /v1.43/containers/json` and `GET /v1.43/containers/{id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200`. Nothing else, ever: no inspect (`/containers/{id}/json` returns the environment, which holds `RUNNER_TOKEN`), no exec, attach, archive, start, stop, kill, no POST, PUT or DELETE.
- Pipe: `\\.\pipe\docker_engine`.
- A runner image name (registry, owner, tag and digest removed, lowercased) contains `actions-runner`, `github-runner` or `gh-runner`. The tag is never used.
- Setting key `watchers_ci_runners`, absent = off. Control name "Show my CI runners' jobs".
- Card: id `r:<runner>:<job>`, title `<runner> · <job>`, short title the job clipped to `WatcherLimits.Short` (12), note `CI runner`, no link, start time from the runner's own stamp.
- Result words: `Succeeded`, `SucceededWithIssues` are passed; `Failed`, `Abandoned` are failed; `Canceled`, `Skipped` and anything unknown are finished.
- Only `Listening for Jobs` marks a runner (re)start. `Runner reconnected.` is noise: it can appear mid-job after a network blip and must not end a card.
- Runner cards never lose touch. Demo mode hides them. Failure glows once (where the Watchers piece is placed, like waiting).
- Replay: lines stamped more than 30 s before the follow opened put up no card for a job that already ended; a job still open when the replay ends gets its card with its real start.
- Status lines (exact): `Watching {n} runners: {names}` (singular `Watching 1 runner: {name}`), `No runner containers are running.`, `Docker isn't running.`, `Docker refused the connection. Your Windows account needs to be in the docker-users group.`
- Nothing new on disk except the setting. No log line per retry; one log line when the Docker state changes; never a job name in a log.
- Working tree is CRLF with an LF index: never `sed -i` a tracked file.
- Run tests from `windows-dotnet/`. Quit `Sanduhr.exe` before building the App if it is running.

## Review Focus

1. **A runner card with an unbounded window must not overflow the clock.** `Touched + Window` with `TimeSpan.MaxValue` throws; `Window(Runner)` returns null and `Tick`/`NextChange` skip it. Pinned in Task 5 (`RunnerCardNeverLosesTouch_AndSchedulesNothing`).
2. **Job names with spaces, parentheses, a colon, or the words "completed with result" inside.** Matrix jobs look like `test (ubuntu-latest, 3.12)`. Pinned in Task 1 (`ParsesAwkwardJobNames`).
3. **Frames and lines split at any byte, and a line split across two frames.** Docker does not promise one line per frame. Pinned in Task 3 (`EveryByteSplitGivesTheSameLines`, `LineAcrossTwoFrames`).
4. **Docker access denied** (the account is not in `docker-users`): the status says so, nothing throws, nothing retries loudly. Pinned in Task 7 (`AccessDeniedShowsTheGroupLine`).
5. **A container restarts while followed:** its log stream ends, the service follows again, and the replayed tail (old `Running job` lines, then `Listening for Jobs`) must neither duplicate the card nor bring a finished one back. Pinned in Task 2 (`RestartInReplayLeavesNothingOpen`) and Task 7 (`StreamEndRefollowsWithoutDuplicates`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/RunnerLog.cs` (create) | `RunnerLine` records and `RunnerLog.Parse`: one log line to a start, end, online or nothing. |
| `src/Sanduhr.Core/RunnerFollow.cs` (create) | Per-runner replay gate. |
| `src/Sanduhr.Core/DockerLogFrames.cs` (create) | Docker's multiplexed stream to whole lines. |
| `src/Sanduhr.Core/DockerContainers.cs` (create) | List JSON to containers; runner image match; runner name; `RunnerStatus` text. |
| `src/Sanduhr.Core/Watchers.cs` (modify) | `WatcherSource.Runner`, `Watcher.Runner`/`Job`, board runner operations, nullable window, demo hiding, spoken text. |
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | `CiRunnersSwitch`. |
| `src/Sanduhr.App/Services/SettingsStore.cs` (modify) | `LoadWatchersCiRunners` / `SaveWatchersCiRunners`. |
| `src/Sanduhr.App/Desk/DockerPipe.cs` (create) | `IDockerApi` and its only implementation; error classification. |
| `src/Sanduhr.App/Desk/RunnerWatchService.cs` (create) | Lists, follows, applies, reports status. |
| `src/Sanduhr.App/Desk/DeskHandoffSeams.cs` (modify) | `WatcherStore.ApplyRunner`, `RunnerGone`, `ClearRunners`, `FailedStarted`. |
| `src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (modify) | `WatchersCiRunners`, `CiRunnersStatus`. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml` (modify) | The switch and the live status line. |
| `src/Sanduhr.App/App.xaml.cs` (modify) | Wiring. |
| `tests/Sanduhr.Tests/fixtures/runner-logs/*.log` (create) | Real and derived runner lines. |
| `tests/Sanduhr.Tests/RunnerLogTests.cs`, `RunnerFollowTests.cs`, `DockerLogFramesTests.cs`, `DockerContainersTests.cs`, `RunnerBoardTests.cs` (create) | Core tests. |
| `tests/Sanduhr.App.Tests/DockerPipeBoundaryTests.cs`, `RunnerWatchServiceTests.cs` (create) | App tests. |
| `docs/PRIVACY.md`, `CHANGELOG.md`, `docs/store/reviewer-letter-3.9.0.0.md`, `windows-dotnet/README.md`, `docs/ci-watchers-3.9-spec.md` | Docs. |

---

### Task 1: Runner log fixtures and `RunnerLog.Parse`

**Files:**
- Create: `windows-dotnet/tests/Sanduhr.Tests/fixtures/runner-logs/neb-alpha-succeeded.log`
- Create: `windows-dotnet/tests/Sanduhr.Tests/fixtures/runner-logs/neb-reconnect-noise.log`
- Create: `windows-dotnet/tests/Sanduhr.Tests/fixtures/runner-logs/derived.log`
- Create: `windows-dotnet/src/Sanduhr.Core/RunnerLog.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/RunnerLogTests.cs`

**Interfaces:**
- Produces: `abstract record RunnerLine(DateTimeOffset At)` with nested `RunnerLine.JobStarted(string Job, DateTimeOffset At)`, `RunnerLine.JobEnded(string Job, WatcherState Result, DateTimeOffset At)`, `RunnerLine.Online(DateTimeOffset At)`; `static RunnerLine? RunnerLog.Parse(string line)`; `static WatcherState RunnerLog.Result(string word)`.

The fixtures already copy with `<None Include="fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />` in `Sanduhr.Tests.csproj`.

- [ ] **Step 1: Write the fixtures**

`neb-alpha-succeeded.log` (real, runner names generalized, `ci-runners-alpha-1-1`, run of 2026-10-22):

```
# real: captured on the owner's Neb machine, runner names generalized (ci-runners-alpha-1-1), 2026-10-22
2026-10-22T01:59:10.762174669Z 2026-10-22 01:59:10Z: Listening for Jobs
2026-10-22T02:00:03.736011454Z 2026-10-22 02:00:03Z: Running job: compile
2026-10-22T02:00:18.054757098Z 2026-10-22 02:00:18Z: Job compile completed with result: Succeeded
2026-10-22T02:00:19.799051744Z 2026-10-22 02:00:19Z: Running job: test
2026-10-22T02:01:58.598577583Z 2026-10-22 02:01:58Z: Job test completed with result: Succeeded
```

`neb-reconnect-noise.log` (real, runner names generalized, `ci-runners-bravo-1-1`, 2026-10-23, blank lines dropped):

```
# real: captured on the owner's Neb machine, runner names generalized (ci-runners-bravo-1-1), 2026-10-23
2026-10-23T18:54:52.723113448Z A session for this runner already exists.
2026-10-23T18:54:52.723263041Z 2026-10-23 18:54:52Z: Runner connect error: Error: Conflict. Retrying until reconnected.
2026-10-23T18:55:23.830324801Z √ Connected to GitHub
2026-10-23T18:55:55.875739249Z 2026-10-23 18:55:55Z: Runner reconnected.
2026-10-23T18:55:55.876180740Z Current runner version: '2.337.0'
2026-10-23T18:55:55.876189246Z 2026-10-23 18:55:55Z: Listening for Jobs
```

`derived.log` (same line shape, result word or name swapped; decision FdHYu1NknZneGkE71adp):

```
# derived: the real line shape with the result word or job name swapped. Replace with real lines when captured.
2026-10-22T02:00:03.736011454Z 2026-10-22 02:00:03Z: Running job: build
2026-10-22T02:00:18.054757098Z 2026-10-22 02:00:18Z: Job build completed with result: Failed
2026-10-22T02:00:19.799051744Z 2026-10-22 02:00:19Z: Running job: lint
2026-10-22T02:00:30.598577583Z 2026-10-22 02:00:30Z: Job lint completed with result: Canceled
2026-10-22T02:00:31.598577583Z 2026-10-22 02:00:31Z: Running job: docs
2026-10-22T02:00:40.598577583Z 2026-10-22 02:00:40Z: Job docs completed with result: SucceededWithIssues
2026-10-22T02:00:41.598577583Z 2026-10-22 02:00:41Z: Running job: deploy
2026-10-22T02:00:42.598577583Z 2026-10-22 02:00:42Z: Job deploy completed with result: Skipped
2026-10-22T02:00:43.598577583Z 2026-10-22 02:00:43Z: Running job: e2e
2026-10-22T02:00:50.598577583Z 2026-10-22 02:00:50Z: Job e2e completed with result: Abandoned
2026-10-22T02:01:00.598577583Z 2026-10-22 02:01:00Z: Running job: test (ubuntu-latest, 3.12)
2026-10-22T02:02:00.598577583Z 2026-10-22 02:02:00Z: Job test (ubuntu-latest, 3.12) completed with result: Succeeded
2026-10-22T02:03:00.598577583Z 2026-10-22 02:03:00Z: Running job: stuck
2026-10-22T02:09:00.598577583Z 2026-10-22 02:09:00Z: Listening for Jobs
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Runner log lines (spec ci-watchers-3.9 sections 4 and 7): the real lines captured on Neb and
/// the derived ones, each parsed to a job start, a job end, the runner coming online, or nothing.</summary>
public class RunnerLogTests
{
    private static IEnumerable<string> Fixture(string name) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "runner-logs", name))
            .Where(l => l.Length > 0 && !l.StartsWith('#'));

    private static DateTimeOffset Utc(int h, int m, int s, int day = 22) => new(2026, 10, day, h, m, s, TimeSpan.Zero);

    [Fact]
    public void RealSucceededRun()
    {
        var parsed = Fixture("neb-alpha-succeeded.log").Select(RunnerLog.Parse).ToList();
        Assert.Equal(
            new RunnerLine?[]
            {
                new RunnerLine.Online(Utc(1, 59, 10)),
                new RunnerLine.JobStarted("compile", Utc(2, 0, 3)),
                new RunnerLine.JobEnded("compile", WatcherState.Passed, Utc(2, 0, 18)),
                new RunnerLine.JobStarted("test", Utc(2, 0, 19)),
                new RunnerLine.JobEnded("test", WatcherState.Passed, Utc(2, 1, 58)),
            },
            parsed);
    }

    [Fact]
    public void RealReconnectNoiseIsNothingButTheLastLine()
    {
        var parsed = Fixture("neb-reconnect-noise.log").Select(RunnerLog.Parse).ToList();
        Assert.All(parsed.Take(5), Assert.Null);
        Assert.Equal(new RunnerLine.Online(Utc(18, 55, 55, day: 23)), parsed[5]);
    }

    [Theory]
    [InlineData("Succeeded", WatcherState.Passed)]
    [InlineData("SucceededWithIssues", WatcherState.Passed)]
    [InlineData("Failed", WatcherState.Failed)]
    [InlineData("Abandoned", WatcherState.Failed)]
    [InlineData("Canceled", WatcherState.Finished)]
    [InlineData("Skipped", WatcherState.Finished)]
    [InlineData("SomethingNew", WatcherState.Finished)]
    public void ResultWords(string word, WatcherState expected) => Assert.Equal(expected, RunnerLog.Result(word));

    [Fact]
    public void DerivedLinesParse()
    {
        var ends = Fixture("derived.log").Select(RunnerLog.Parse).OfType<RunnerLine.JobEnded>()
            .Select(e => (e.Job, e.Result)).ToList();
        Assert.Equal(
            new[]
            {
                ("build", WatcherState.Failed), ("lint", WatcherState.Finished), ("docs", WatcherState.Passed),
                ("deploy", WatcherState.Finished), ("e2e", WatcherState.Failed),
                ("test (ubuntu-latest, 3.12)", WatcherState.Passed),
            },
            ends);
    }

    [Fact]
    public void ParsesAwkwardJobNames()
    {
        const string stamp = "2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: ";
        Assert.Equal(new RunnerLine.JobStarted("deploy: prod (eu, 2)", Utc(2, 0, 3)),
            RunnerLog.Parse(stamp + "Running job: deploy: prod (eu, 2)"));
        Assert.Equal(new RunnerLine.JobEnded("Job x completed with result: y", WatcherState.Failed, Utc(2, 0, 3)),
            RunnerLog.Parse(stamp + "Job Job x completed with result: y completed with result: Failed"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Running job: compile")] // no stamps
    [InlineData("2026-10-22T02:00:03.7Z Running job: compile")] // no runner stamp
    [InlineData("2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running job: ")] // empty name
    [InlineData("2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Runner reconnected.")] // can happen mid-job
    [InlineData("2026-10-22T02:00:03.7Z 2026-13-40 02:00:03Z: Listening for Jobs")] // impossible date
    public void Nothing(string line) => Assert.Null(RunnerLog.Parse(line));

    [Fact]
    public void LongNamesAreClippedTheSameAtStartAndEnd()
    {
        var name = new string('a', 200);
        const string stamp = "2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: ";
        var start = (RunnerLine.JobStarted)RunnerLog.Parse(stamp + "Running job: " + name)!;
        var end = (RunnerLine.JobEnded)RunnerLog.Parse(stamp + $"Job {name} completed with result: Succeeded")!;
        Assert.Equal(start.Job, end.Job);
        Assert.True(start.Job.Length <= WatcherLimits.Title);
    }

    [Fact]
    public void TrailingCarriageReturnIsIgnored() =>
        Assert.Equal(new RunnerLine.JobStarted("compile", Utc(2, 0, 3)),
            RunnerLog.Parse("2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running job: compile\r"));
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~RunnerLogTests`
Expected: build FAILS with `The type or namespace name 'RunnerLine' could not be found`.

- [ ] **Step 4: Implement `RunnerLog.cs`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>What one line of a GitHub Actions runner's console says, when it says anything Sanduhr
/// uses. <see cref="At"/> is the runner's own stamp (whole seconds, UTC).</summary>
public abstract record RunnerLine(DateTimeOffset At)
{
    /// <summary>"Running job: &lt;name&gt;".</summary>
    public sealed record JobStarted(string Job, DateTimeOffset At) : RunnerLine(At);

    /// <summary>"Job &lt;name&gt; completed with result: &lt;word&gt;".</summary>
    public sealed record JobEnded(string Job, WatcherState Result, DateTimeOffset At) : RunnerLine(At);

    /// <summary>"Listening for Jobs": the runner (re)started and holds no job.</summary>
    public sealed record Online(DateTimeOffset At) : RunnerLine(At);
}

/// <summary>
/// A runner container's log line as Docker hands it over with timestamps on (spec ci-watchers-3.9,
/// section 4): <c>&lt;Docker RFC 3339 stamp&gt; &lt;runner stamp "yyyy-MM-dd HH:mm:ssZ"&gt;: &lt;text&gt;</c>.
/// Pure. Every other line (reconnect noise, the version banner, "Runner reconnected.", which can
/// come mid-job) is nothing.
/// </summary>
public static partial class RunnerLog
{
    /// <summary>Longer lines are not the runner's own and are ignored.</summary>
    public const int MaxLine = 4096;

    public static RunnerLine? Parse(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length > MaxLine) return null;
        var m = LinePattern().Match(line.TrimEnd('\r', '\n'));
        if (!m.Success) return null;
        if (!DateTimeOffset.TryParseExact(m.Groups["at"].Value, "yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
            return null;
        var text = m.Groups["text"].Value;
        if (StartPattern().Match(text) is { Success: true } s)
            return WatcherLimits.Line(s.Groups["job"].Value, WatcherLimits.Title) is { } job ? new RunnerLine.JobStarted(job, at) : null;
        if (EndPattern().Match(text) is { Success: true } e)
            return WatcherLimits.Line(e.Groups["job"].Value, WatcherLimits.Title) is { } job
                ? new RunnerLine.JobEnded(job, Result(e.Groups["result"].Value), at)
                : null;
        return text == "Listening for Jobs" ? new RunnerLine.Online(at) : null;
    }

    /// <summary>The runner's result word as a watcher state: passed, failed, or finished (ended, nothing
    /// to celebrate or mourn, including any word this build does not know).</summary>
    public static WatcherState Result(string word) => word switch
    {
        "Succeeded" or "SucceededWithIssues" => WatcherState.Passed,
        "Failed" or "Abandoned" => WatcherState.Failed,
        _ => WatcherState.Finished,
    };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z (?<at>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z): (?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"^Running job: (?<job>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex StartPattern();

    [GeneratedRegex(@"^Job (?<job>.+) completed with result: (?<result>[A-Za-z]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EndPattern();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~RunnerLogTests`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/RunnerLog.cs windows-dotnet/tests/Sanduhr.Tests/RunnerLogTests.cs windows-dotnet/tests/Sanduhr.Tests/fixtures/runner-logs
git commit -m "feat(ci-watchers): parse runner log lines, with real fixtures from Neb"
```

---

### Task 2: `RunnerFollow`, the replay gate

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/RunnerFollow.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/RunnerFollowTests.cs`

**Interfaces:**
- Consumes: `RunnerLine` (Task 1).
- Produces: `sealed class RunnerFollow(DateTimeOffset opened)` with `static readonly TimeSpan ReplayMargin` (30 s), `bool Live`, `IReadOnlyList<RunnerLine> Feed(RunnerLine line)`, `IReadOnlyList<RunnerLine> EndReplay()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The replay gate (spec ci-watchers-3.9 section 5, "Starting mid-job"): the tail Docker
/// replays rebuilds the open job but never puts up a card for one that already ended.</summary>
public class RunnerFollowTests
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);
    private static RunnerLine.JobStarted Start(string job, int secondsBeforeOpen) => new(job, Opened.AddSeconds(-secondsBeforeOpen));
    private static RunnerLine.JobEnded End(string job, int secondsBeforeOpen, WatcherState r = WatcherState.Passed) => new(job, r, Opened.AddSeconds(-secondsBeforeOpen));
    private static RunnerLine.Online Online(int secondsBeforeOpen) => new(Opened.AddSeconds(-secondsBeforeOpen));

    [Fact]
    public void FinishedJobsInTheReplayPutUpNothing()
    {
        var f = new RunnerFollow(Opened);
        Assert.Empty(f.Feed(Start("compile", 600)));
        Assert.Empty(f.Feed(End("compile", 590)));
        Assert.Empty(f.EndReplay());
        Assert.True(f.Live);
    }

    [Fact]
    public void AnOpenJobAtTheEndOfTheReplayComesOutWithItsRealStart()
    {
        var f = new RunnerFollow(Opened);
        f.Feed(Start("compile", 600));
        f.Feed(End("compile", 590));
        f.Feed(Start("test", 300));
        Assert.Equal(new RunnerLine[] { Start("test", 300) }, f.EndReplay());
        Assert.Empty(f.EndReplay()); // once
    }

    [Fact]
    public void RestartInReplayLeavesNothingOpen()
    {
        var f = new RunnerFollow(Opened);
        f.Feed(Start("stuck", 900));
        f.Feed(Online(800));
        Assert.Empty(f.EndReplay());
    }

    [Fact]
    public void ALiveLineEndsTheReplayFirst()
    {
        var f = new RunnerFollow(Opened);
        f.Feed(Start("test", 300));
        var end = End("test", -5);
        Assert.Equal(new RunnerLine[] { Start("test", 300), end }, f.Feed(end));
        Assert.True(f.Live);
    }

    [Fact]
    public void WithinTheMarginIsLive()
    {
        var f = new RunnerFollow(Opened);
        var start = Start("test", 29);
        Assert.Equal(new RunnerLine[] { start }, f.Feed(start));
    }

    [Fact]
    public void AfterReplayEveryLinePassesThrough()
    {
        var f = new RunnerFollow(Opened);
        f.EndReplay();
        var old = End("x", 3600);
        Assert.Equal(new RunnerLine[] { old }, f.Feed(old));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~RunnerFollowTests`
Expected: build FAILS with `The type or namespace name 'RunnerFollow' could not be found`.

- [ ] **Step 3: Implement `RunnerFollow.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>
/// One runner's log, followed (spec ci-watchers-3.9 section 5). Docker sends the last lines first;
/// lines stamped more than <see cref="ReplayMargin"/> before the follow opened are that replay. They
/// only rebuild which job is open: a job that ended in the replay puts up no card, and a job still
/// open when the replay ends comes out once, with its real start. The replay ends at the first live
/// line or when the caller says so (<see cref="EndReplay"/>, a moment after the stream opened, for an
/// idle runner that prints nothing new). Pure.
/// </summary>
public sealed class RunnerFollow(DateTimeOffset opened)
{
    public static readonly TimeSpan ReplayMargin = TimeSpan.FromSeconds(30);

    private RunnerLine.JobStarted? _open;

    public bool Live { get; private set; }

    /// <summary>What the board should apply for <paramref name="line"/>: nothing during the replay, the
    /// line itself once live (after the open job, when this line ends the replay).</summary>
    public IReadOnlyList<RunnerLine> Feed(RunnerLine line)
    {
        if (Live) return [line];
        if (line.At >= opened - ReplayMargin) return [.. EndReplay(), line];
        _open = line switch
        {
            RunnerLine.JobStarted s => s,
            RunnerLine.JobEnded or RunnerLine.Online => null,
            _ => _open,
        };
        return [];
    }

    /// <summary>Ends the replay: the job still open, if any, once.</summary>
    public IReadOnlyList<RunnerLine> EndReplay()
    {
        if (Live) return [];
        Live = true;
        var open = _open;
        _open = null;
        return open is null ? [] : [open];
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~RunnerFollowTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/RunnerFollow.cs windows-dotnet/tests/Sanduhr.Tests/RunnerFollowTests.cs
git commit -m "feat(ci-watchers): replay gate so starting Sanduhr shows only open jobs"
```

---

### Task 3: `DockerLogFrames`

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DockerLogFrames.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DockerLogFramesTests.cs`

**Interfaces:**
- Produces: `sealed class DockerLogFrames` with `const int MaxFrame = 65536`, `const int MaxLine = 16384`, `IReadOnlyList<string> Push(ReadOnlySpan<byte> data)`.

HTTP chunked encoding is handled by `HttpClient` in Task 6; this class sees the already de-chunked body: frames of an 8-byte header (byte 0 the stream, 1 stdout or 2 stderr; bytes 1 to 3 zero; bytes 4 to 7 the payload length, big-endian) followed by the payload.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Buffers.Binary;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Docker's multiplexed log stream (no TTY) into whole lines (spec ci-watchers-3.9 section 4).</summary>
public class DockerLogFramesTests
{
    private static byte[] Frame(string text, byte stream = 1)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[8 + payload.Length];
        frame[0] = stream;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    private static readonly string[] Lines =
    [
        "2026-10-22T02:00:03.736011454Z 2026-10-22 02:00:03Z: Running job: compile",
        "2026-10-22T02:00:18.054757098Z 2026-10-22 02:00:18Z: Job compile completed with result: Succeeded",
    ];

    private static byte[] Stream() => [.. Frame(Lines[0] + "\n"), .. Frame(Lines[1] + "\n", stream: 2)];

    [Fact]
    public void WholeFramesInOneRead() => Assert.Equal(Lines, new DockerLogFrames().Push(Stream()));

    [Fact]
    public void EveryByteSplitGivesTheSameLines()
    {
        var bytes = Stream();
        for (var cut = 1; cut < bytes.Length; cut++)
        {
            var d = new DockerLogFrames();
            var got = d.Push(bytes.AsSpan(0, cut)).Concat(d.Push(bytes.AsSpan(cut))).ToList();
            Assert.Equal(Lines, got);
        }
    }

    [Fact]
    public void ByteAtATime()
    {
        var d = new DockerLogFrames();
        var got = Stream().SelectMany(b => d.Push([b])).ToList();
        Assert.Equal(Lines, got);
    }

    [Fact]
    public void LineAcrossTwoFrames()
    {
        var d = new DockerLogFrames();
        Assert.Empty(d.Push(Frame("2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running ")));
        Assert.Equal(["2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running job: compile"], d.Push(Frame("job: compile\n")));
    }

    [Fact]
    public void CarriageReturnsAndEmptyFrames()
    {
        var d = new DockerLogFrames();
        Assert.Equal(["a", "", "b"], d.Push([.. Frame(""), .. Frame("a\r\n\nb\n")]));
    }

    [Fact]
    public void OversizedFrameIsSkippedAndTheNextOneReads()
    {
        var big = new byte[8 + DockerLogFrames.MaxFrame + 1];
        big[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(big.AsSpan(4), (uint)(DockerLogFrames.MaxFrame + 1));
        big.AsSpan(8).Fill((byte)'x');
        Assert.Equal(["ok"], new DockerLogFrames().Push([.. big, .. Frame("ok\n")]));
    }

    [Fact]
    public void StdinOrUnknownStreamIsSkipped() =>
        Assert.Equal(["ok"], new DockerLogFrames().Push([.. Frame("secret\n", stream: 0), .. Frame("ok\n", stream: 1)]));

    [Fact]
    public void OverlongLineIsDropped()
    {
        var d = new DockerLogFrames();
        Assert.Equal(["after"], d.Push([.. Frame(new string('x', DockerLogFrames.MaxLine + 10) + "\n"), .. Frame("after\n")]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DockerLogFramesTests`
Expected: build FAILS with `The type or namespace name 'DockerLogFrames' could not be found`.

- [ ] **Step 3: Implement `DockerLogFrames.cs`**

```csharp
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Docker's log stream for a container without a TTY (spec ci-watchers-3.9 section 4): frames of an
/// 8-byte header (the stream, 1 stdout or 2 stderr; three zero bytes; the payload length, big-endian)
/// and the payload, decoded into whole lines. Frames and lines may split anywhere. A frame over
/// <see cref="MaxFrame"/> or from any other stream is skipped; a line over <see cref="MaxLine"/> is
/// dropped. Pure.
/// </summary>
public sealed class DockerLogFrames
{
    public const int MaxFrame = 64 * 1024;
    public const int MaxLine = 16 * 1024;

    private readonly byte[] _header = new byte[8];
    private int _headerFill;
    private long _payloadLeft;
    private bool _skipping;
    private readonly List<byte> _line = [];
    private bool _lineTooLong;

    /// <summary>The lines <paramref name="data"/> completes, without their line ends.</summary>
    public IReadOnlyList<string> Push(ReadOnlySpan<byte> data)
    {
        var lines = new List<string>();
        while (data.Length > 0)
        {
            if (_payloadLeft == 0)
            {
                var take = Math.Min(_header.Length - _headerFill, data.Length);
                data[..take].CopyTo(_header.AsSpan(_headerFill));
                _headerFill += take;
                data = data[take..];
                if (_headerFill < _header.Length) break;
                _headerFill = 0;
                _payloadLeft = BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(4));
                _skipping = _header[0] is not (1 or 2) || _payloadLeft > MaxFrame;
                continue;
            }
            var n = (int)Math.Min(_payloadLeft, data.Length);
            if (!_skipping) Append(data[..n], lines);
            _payloadLeft -= n;
            data = data[n..];
        }
        return lines;
    }

    private void Append(ReadOnlySpan<byte> bytes, List<string> lines)
    {
        foreach (var b in bytes)
        {
            if (b == (byte)'\n')
            {
                if (!_lineTooLong) lines.Add(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(_line)).TrimEnd('\r'));
                _line.Clear();
                _lineTooLong = false;
            }
            else if (_line.Count < MaxLine)
            {
                _line.Add(b);
            }
            else
            {
                _lineTooLong = true;
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DockerLogFramesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DockerLogFrames.cs windows-dotnet/tests/Sanduhr.Tests/DockerLogFramesTests.cs
git commit -m "feat(ci-watchers): decode Docker's multiplexed log stream into lines"
```

---

### Task 4: `DockerContainers` and `RunnerStatus`

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DockerContainers.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DockerContainersTests.cs`

**Interfaces:**
- Produces: `sealed record DockerContainer(string Id, string Image, string Name, bool Running)`; `static IReadOnlyList<DockerContainer> DockerContainers.Parse(ReadOnlySpan<byte> json)`; `static bool DockerContainers.IsRunnerImage(string image)`; `static IReadOnlyList<DockerContainer> DockerContainers.Runners(IReadOnlyList<DockerContainer> all)`; `const int DockerContainers.MaxBytes`; `enum RunnerStatusKind { Off, Watching, NoRunners, DockerDown, DockerDenied }`; `sealed record RunnerStatus(RunnerStatusKind Kind, IReadOnlyList<string> Runners)` with `string Text` and `static RunnerStatus Off`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The container list (spec ci-watchers-3.9 sections 2, 3 and 5): only the fields the list
/// call returns, runners picked by image name, and the status line under the switch.</summary>
public class DockerContainersTests
{
    private const string Id1 = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Id2 = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string Id3 = "3333333333333333333333333333333333333333333333333333333333333333";

    // The shape of GET /v1.43/containers/json on Neb, 2026-10-23, trimmed to the fields Sanduhr reads.
    private static readonly string Json = $$"""
        [
          {"Id":"{{Id1}}","Names":["/ci-runners-alpha-1-1"],"Image":"dunder-gh-runner:2.337.0","State":"running",
           "Labels":{"com.docker.compose.project":"ci-runners","com.docker.compose.service":"alpha-1"}},
          {"Id":"{{Id2}}","Names":["/ci-runners-egress-proxy-1"],"Image":"dunder-egress-proxy:1","State":"running",
           "Labels":{"com.docker.compose.service":"egress-proxy"}},
          {"Id":"{{Id3}}","Names":["/my-runner"],"Image":"ghcr.io/actions/actions-runner:latest","State":"exited","Labels":{}}
        ]
        """;

    [Fact]
    public void ParsesTheListCall()
    {
        var all = DockerContainers.Parse(Encoding.UTF8.GetBytes(Json));
        Assert.Equal(
            new[]
            {
                new DockerContainer(Id1, "dunder-gh-runner:2.337.0", "alpha-1", true),
                new DockerContainer(Id2, "dunder-egress-proxy:1", "egress-proxy", true),
                new DockerContainer(Id3, "ghcr.io/actions/actions-runner:latest", "my-runner", false),
            },
            all);
    }

    [Fact]
    public void RunnersAreRunningContainersWithARunnerImage() =>
        Assert.Equal(["alpha-1"], DockerContainers.Runners(DockerContainers.Parse(Encoding.UTF8.GetBytes(Json))).Select(c => c.Name));

    [Theory]
    [InlineData("dunder-gh-runner:2.337.0", true)]
    [InlineData("dunder-gh-runner:2.338.0", true)]
    [InlineData("ghcr.io/actions/actions-runner:2.321.0", true)]
    [InlineData("myoung34/github-runner:latest", true)]
    [InlineData("summerwind/actions-runner@sha256:0123abcd", true)]
    [InlineData("localhost:5000/team/GH-Runner", true)]
    [InlineData("dunder-egress-proxy:1", false)]
    [InlineData("postgres:16", false)]
    [InlineData("registry.gh-runner.example.com/postgres:16", false)] // only the image's own name counts
    [InlineData("", false)]
    public void RunnerImages(string image, bool expected) => Assert.Equal(expected, DockerContainers.IsRunnerImage(image));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"Id\":\"short\",\"Image\":\"dunder-gh-runner:1\",\"State\":\"running\"}]")]
    public void MalformedIsEmpty(string json) => Assert.Empty(DockerContainers.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ANameIsOneShortLine()
    {
        var json = $$"""[{"Id":"{{Id1}}","Names":["/x"],"Image":"gh-runner","State":"running","Labels":{"com.docker.compose.service":"{{new string('n', 80)}}"}}]""";
        Assert.True(DockerContainers.Parse(Encoding.UTF8.GetBytes(json))[0].Name.Length <= 40);
    }

    [Fact]
    public void StatusLines()
    {
        Assert.Equal("", RunnerStatus.Off.Text);
        Assert.Equal("Watching 1 runner: alpha-1", new RunnerStatus(RunnerStatusKind.Watching, ["alpha-1"]).Text);
        Assert.Equal("Watching 2 runners: echo-1, echo-2", new RunnerStatus(RunnerStatusKind.Watching, ["echo-1", "echo-2"]).Text);
        Assert.Equal("No runner containers are running.", new RunnerStatus(RunnerStatusKind.NoRunners, []).Text);
        Assert.Equal("Docker isn't running.", new RunnerStatus(RunnerStatusKind.DockerDown, []).Text);
        Assert.Equal("Docker refused the connection. Your Windows account needs to be in the docker-users group.",
            new RunnerStatus(RunnerStatusKind.DockerDenied, []).Text);
    }

    [Fact]
    public void ALongRunnerListIsClipped()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"runner-number-{i}").ToList();
        var text = new RunnerStatus(RunnerStatusKind.Watching, names).Text;
        Assert.StartsWith("Watching 30 runners: runner-number-1, ", text);
        Assert.True(text.Length <= RunnerStatus.MaxText);
        Assert.EndsWith("…", text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DockerContainersTests`
Expected: build FAILS with `The type or namespace name 'DockerContainer' could not be found`.

- [ ] **Step 3: Implement `DockerContainers.cs`**

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>One container from Docker's list call. Only these four fields are kept.</summary>
public sealed record DockerContainer(string Id, string Image, string Name, bool Running);

/// <summary>
/// The answer to <c>GET /containers/json</c> (spec ci-watchers-3.9 sections 2 and 3). The list call
/// carries no environment (verified on Neb, 2026-10-23); Sanduhr reads id, image, names, state and the
/// Compose service label from it and nothing else. A runner is a running container whose image's own
/// name (no registry, owner, tag or digest) contains one of <see cref="RunnerImageWords"/>. Pure.
/// </summary>
public static partial class DockerContainers
{
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>The runner's name on a card at most this long.</summary>
    public const int NameLength = 40;

    public static readonly IReadOnlyList<string> RunnerImageWords = ["actions-runner", "github-runner", "gh-runner"];

    public static IReadOnlyList<DockerContainer> Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxBytes) return [];
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json.ToArray()); }
        catch (JsonException) { return []; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<DockerContainer>();
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                if (Str(c, "Id") is not { } id || !IdPattern().IsMatch(id)) continue;
                var image = Str(c, "Image") ?? "";
                var service = c.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object
                    ? Str(labels, "com.docker.compose.service")
                    : null;
                var first = c.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array
                    ? names.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? n.GetString() : null).FirstOrDefault()
                    : null;
                var name = WatcherLimits.Line(service ?? first?.TrimStart('/'), NameLength) ?? id[..12];
                list.Add(new DockerContainer(id, image, name, Str(c, "State") == "running"));
            }
            return list;
        }
    }

    public static IReadOnlyList<DockerContainer> Runners(IReadOnlyList<DockerContainer> all) =>
        all.Where(c => c.Running && IsRunnerImage(c.Image)).ToList();

    public static bool IsRunnerImage(string image)
    {
        var s = image;
        var at = s.IndexOf('@');
        if (at >= 0) s = s[..at];
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        s = s.ToLowerInvariant();
        return s.Length > 0 && RunnerImageWords.Any(s.Contains);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    internal static partial Regex IdPattern();
}

public enum RunnerStatusKind { Off, Watching, NoRunners, DockerDown, DockerDenied }

/// <summary>The one line under "Show my CI runners' jobs" (spec ci-watchers-3.9 section 5).</summary>
public sealed record RunnerStatus(RunnerStatusKind Kind, IReadOnlyList<string> Runners)
{
    public const int MaxText = 120;

    public static RunnerStatus Off { get; } = new(RunnerStatusKind.Off, []);

    public string Text => Kind switch
    {
        RunnerStatusKind.Watching => Clip(Runners.Count == 1
            ? $"Watching 1 runner: {Runners[0]}"
            : $"Watching {Runners.Count} runners: {string.Join(", ", Runners)}"),
        RunnerStatusKind.NoRunners => "No runner containers are running.",
        RunnerStatusKind.DockerDown => "Docker isn't running.",
        RunnerStatusKind.DockerDenied => "Docker refused the connection. Your Windows account needs to be in the docker-users group.",
        _ => "",
    };

    private static string Clip(string s) => s.Length <= MaxText ? s : s[..(MaxText - 1)] + "…";

    public bool Equals(RunnerStatus? other) => other is not null && Kind == other.Kind && Runners.SequenceEqual(other.Runners);

    public override int GetHashCode() => HashCode.Combine(Kind, Runners.Count);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DockerContainersTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DockerContainers.cs windows-dotnet/tests/Sanduhr.Tests/DockerContainersTests.cs
git commit -m "feat(ci-watchers): read Docker's container list and pick runners by image name"
```

---

### Task 5: Runner cards on the board

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/Watchers.cs` (the `WatcherSource` enum near line 66, the `Watcher` record near line 76, `WatcherBoard.Window` near line 250, `Tick` and `NextChange` near lines 360 to 386, `Shown` near line 405, `WatcherText.Spoken` near line 486)
- Test: `windows-dotnet/tests/Sanduhr.Tests/RunnerBoardTests.cs`

**Interfaces:**
- Consumes: `RunnerLine` (Task 1).
- Produces: `WatcherSource.Runner`; `Watcher.Runner` and `Watcher.Job` (`string?`); `readonly record struct RunnerChange(bool Changed, bool Failed)`; `static string WatcherBoard.RunnerId(string runner, string job)`; `RunnerChange WatcherBoard.ApplyRunner(string runner, RunnerLine line, DateTimeOffset now)`; `bool WatcherBoard.RunnerGone(string runner, DateTimeOffset now)`; `static TimeSpan? WatcherBoard.Window(WatcherSource source)` (was non-nullable; no caller outside `Watchers.cs`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Runner cards on the watcher board (spec ci-watchers-3.9 sections 4 and 5).</summary>
public class RunnerBoardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);
    private static RunnerLine.JobStarted Start(string job, int at = 0) => new(job, T0.AddSeconds(at));
    private static RunnerLine.JobEnded End(string job, WatcherState r, int at = 60) => new(job, r, T0.AddSeconds(at));

    [Fact]
    public void AJobPutsUpACard()
    {
        var b = new WatcherBoard();
        var change = b.ApplyRunner("alpha-1", Start("test (ubuntu-latest, 3.12)", at: -30), T0);
        Assert.Equal(new RunnerChange(true, false), change);
        var w = b.Get("r:alpha-1:test (ubuntu-latest, 3.12)")!;
        Assert.Equal(WatcherSource.Runner, w.Source);
        Assert.Equal("alpha-1 · test (ubuntu-latest, 3.12)", w.Title);
        Assert.Equal(WatcherText.Clip("test (ubuntu-latest, 3.12)", WatcherLimits.Short), w.Short);
        Assert.Equal("CI runner", w.Note);
        Assert.Null(w.Link);
        Assert.Equal(WatcherState.Running, w.State);
        Assert.Equal(T0.AddSeconds(-30), w.Started);
        Assert.Equal("alpha-1", w.Runner);
        Assert.Equal("test (ubuntu-latest, 3.12)", w.Job);
    }

    [Theory]
    [InlineData(WatcherState.Passed, false)]
    [InlineData(WatcherState.Failed, true)]
    [InlineData(WatcherState.Finished, false)]
    public void TheResultEndsIt(WatcherState result, bool failed)
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        Assert.Equal(new RunnerChange(true, failed), b.ApplyRunner("echo-1", End("build", result), T0.AddSeconds(60)));
        var w = b.Get("r:echo-1:build")!;
        Assert.Equal(result, w.State);
        Assert.Equal(T0.AddSeconds(60), w.Ended);
    }

    [Fact]
    public void AnEndWithNoCardDoesNothing()
    {
        var b = new WatcherBoard();
        Assert.Equal(new RunnerChange(false, false), b.ApplyRunner("echo-1", End("build", WatcherState.Failed), T0));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void ASecondEndDoesNotGlowAgain()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        b.ApplyRunner("echo-1", End("build", WatcherState.Failed), T0);
        Assert.Equal(new RunnerChange(false, false), b.ApplyRunner("echo-1", End("build", WatcherState.Failed), T0));
    }

    [Fact]
    public void ANewJobOnTheSameRunnerEndsTheOpenOneAsFinished()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        b.ApplyRunner("echo-1", Start("test", at: 30), T0.AddSeconds(30));
        Assert.Equal(WatcherState.Finished, b.Get("r:echo-1:build")!.State);
        Assert.Equal(WatcherState.Running, b.Get("r:echo-1:test")!.State);
    }

    [Fact]
    public void OtherRunnersAreUntouched()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        b.ApplyRunner("echo-2", Start("build"), T0);
        b.ApplyRunner("echo-2", new RunnerLine.Online(T0), T0);
        Assert.Equal(WatcherState.Running, b.Get("r:echo-1:build")!.State);
        Assert.Equal(WatcherState.Finished, b.Get("r:echo-2:build")!.State);
    }

    [Fact]
    public void RunnerGoneEndsItsOpenCard()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        Assert.True(b.RunnerGone("echo-1", T0.AddSeconds(5)));
        Assert.Equal(WatcherState.Finished, b.Get("r:echo-1:build")!.State);
        Assert.False(b.RunnerGone("echo-1", T0.AddSeconds(6)));
    }

    [Fact]
    public void RunnerCardNeverLosesTouch_AndSchedulesNothing()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        Assert.Null(WatcherBoard.Window(WatcherSource.Runner));
        Assert.Null(b.NextChange());
        Assert.False(b.Tick(T0.AddHours(24)));
        Assert.False(b.Tick(DateTimeOffset.MaxValue));
        Assert.Equal(WatcherState.Running, b.Get("r:echo-1:build")!.State);
    }

    [Fact]
    public void PassedRunnerCardsFadeLikeAnyOther()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", Start("build"), T0);
        b.ApplyRunner("echo-1", End("build", WatcherState.Passed), T0);
        Assert.Equal(T0 + WatcherBoard.FadeAfter, b.NextChange());
        Assert.True(b.Tick(T0 + WatcherBoard.FadeAfter));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void DemoModeHidesRunnerCards()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("alpha-1", Start("build"), T0);
        b.Apply(new WatcherCommand.Start("w1", "deploy", null, null, false), T0);
        Assert.Equal(["a:w1"], WatcherBoard.Shown(b.Ordered(), demo: true).Select(w => w.Id));
        Assert.Equal(2, WatcherBoard.Shown(b.Ordered(), demo: false).Count);
    }

    [Fact]
    public void ClearDropsOnlyRunnerCards()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("alpha-1", Start("build"), T0);
        b.Apply(new WatcherCommand.Start("w1", "deploy", null, null, false), T0);
        b.Clear(WatcherSource.Runner);
        Assert.Equal([WatcherSource.Agent], b.Watchers.Select(w => w.Source));
    }

    [Fact]
    public void SpokenNamesTheJobAndRunner()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("alpha-1", Start("test"), T0);
        Assert.Equal("CI job test on alpha-1, running, 3m", WatcherText.Spoken(b.Get("r:alpha-1:test")!, T0.AddMinutes(3)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~RunnerBoardTests`
Expected: build FAILS with `'WatcherBoard' does not contain a definition for 'ApplyRunner'`.

- [ ] **Step 3: Add the source, the two fields and `RunnerChange`**

In `Watchers.cs`, extend the enum:

```csharp
/// <summary>Who put the watcher up.</summary>
public enum WatcherSource
{
    /// <summary>An agent, through the MCP tools.</summary>
    Agent,

    /// <summary>Claude Code's background work, through the Stop hook.</summary>
    Automatic,

    /// <summary>A job a GitHub Actions runner container on this PC ran, read from its log (spec ci-watchers-3.9).</summary>
    Runner,
}
```

In the `Watcher` record, after `Kind`:

```csharp
    /// <summary>A runner card's runner (the Compose service name).</summary>
    public string? Runner { get; init; }

    /// <summary>A runner card's job name.</summary>
    public string? Job { get; init; }
```

After the `StopReport` record:

```csharp
/// <summary>What a runner line did to the board: anything at all, and whether a card just failed (the glow fires once).</summary>
public readonly record struct RunnerChange(bool Changed, bool Failed);
```

- [ ] **Step 4: Make the window nullable and skip it in `Tick` and `NextChange`**

Replace `Window`:

```csharp
    /// <summary>How long a running watcher may go unheard before it loses touch; null for a runner's,
    /// which prints nothing while a job runs and ends only on its result, a new job or a restart.</summary>
    public static TimeSpan? Window(WatcherSource source) => source switch
    {
        WatcherSource.Agent => AgentWindow,
        WatcherSource.Automatic => AutomaticWindow,
        _ => null,
    };
```

In `Tick`, replace the lost-touch condition:

```csharp
            if (w.State is WatcherState.Running or WatcherState.Waiting
                && Window(w.Source) is { } window && now - w.Touched > window)
                _watchers[i] = w with { State = WatcherState.LostTouch };
```

In `NextChange`, replace the running branch:

```csharp
            else if (w.State is WatcherState.Running or WatcherState.Waiting && Window(w.Source) is { } window)
                at = w.Touched + window + TimeSpan.FromSeconds(1);
```

- [ ] **Step 5: Add the runner operations to `WatcherBoard`** (after `ApplyStop`)

```csharp
    public static string RunnerId(string runner, string job) => $"r:{runner}:{job}";

    /// <summary>A runner's line (spec ci-watchers-3.9 section 5). A start ends the runner's open card as
    /// finished (a runner runs one job at a time) and puts up the new one with the runner's own start
    /// time; an end closes the matching open card; the runner coming online ends its open card.</summary>
    public RunnerChange ApplyRunner(string runner, RunnerLine line, DateTimeOffset now)
    {
        var before = _watchers.ToList();
        var failed = false;
        switch (line)
        {
            case RunnerLine.JobStarted s:
            {
                EndOpenRunnerCards(runner, now);
                var key = RunnerId(runner, s.Job);
                _watchers.RemoveAll(w => w.Id == key);
                _watchers.Add(new Watcher
                {
                    Id = key, Source = WatcherSource.Runner, Title = $"{runner} · {s.Job}",
                    Short = WatcherText.Clip(s.Job, WatcherLimits.Short), Note = "CI runner",
                    State = WatcherState.Running, Started = s.At, Touched = now, Runner = runner, Job = s.Job,
                });
                Trim();
                break;
            }
            case RunnerLine.JobEnded e:
            {
                var i = _watchers.FindIndex(w => w.Id == RunnerId(runner, e.Job) && !w.State.IsEnded());
                if (i < 0) break;
                _watchers[i] = _watchers[i] with { State = e.Result, Touched = now, Ended = now };
                failed = e.Result == WatcherState.Failed;
                break;
            }
            case RunnerLine.Online:
                EndOpenRunnerCards(runner, now);
                break;
        }
        return new RunnerChange(!_watchers.SequenceEqual(before), failed);
    }

    /// <summary>The runner's container stopped or left the list: its open card ends as finished.</summary>
    public bool RunnerGone(string runner, DateTimeOffset now)
    {
        var before = _watchers.ToList();
        EndOpenRunnerCards(runner, now);
        return !_watchers.SequenceEqual(before);
    }

    private void EndOpenRunnerCards(string runner, DateTimeOffset now)
    {
        for (var i = 0; i < _watchers.Count; i++)
        {
            var w = _watchers[i];
            if (w.Source == WatcherSource.Runner && w.Runner == runner && !w.State.IsEnded())
                _watchers[i] = w with { State = WatcherState.Finished, Touched = now, Ended = now };
        }
    }
```

- [ ] **Step 6: Hide runner cards in demo mode and name them for screen readers**

Replace `Shown`:

```csharp
    /// <summary>What the Desk shows: the ordered watchers, less the work ones and the runners' in demo
    /// mode (a runner's name can name a private repo).</summary>
    public static IReadOnlyList<Watcher> Shown(IReadOnlyList<Watcher> ordered, bool demo) =>
        demo ? ordered.Where(w => !w.Work && w.Source != WatcherSource.Runner).ToList() : ordered;
```

Replace the first line of `Spoken`:

```csharp
        var name = w.Source == WatcherSource.Runner && w.Runner is { } runner && w.Job is { } job
            ? $"CI job {job} on {runner}"
            : w.Title;
        var parts = new List<string> { name, w.State.Label(), Elapsed(w.Elapsed(now)) };
```

and in the same method, skip the runner's fixed note so it isn't read twice:

```csharp
        if (w.Note is { } note && w.Source != WatcherSource.Runner) parts.Add(note);
```

- [ ] **Step 7: Run the new and the existing watcher tests**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~RunnerBoardTests|FullyQualifiedName~WatcherTests|FullyQualifiedName~Companion"`
Expected: PASS, the existing `WatcherTests` unchanged.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/Watchers.cs windows-dotnet/tests/Sanduhr.Tests/RunnerBoardTests.cs
git commit -m "feat(ci-watchers): runner cards on the watcher board, never lost touch, hidden in demo mode"
```

---

### Task 6: `DockerPipe` and its boundary test

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/DockerPipe.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DockerPipeBoundaryTests.cs`

**Interfaces:**
- Consumes: `DockerContainers.MaxBytes` (Task 4).
- Produces: `internal interface IDockerApi { Task<byte[]> ListContainersAsync(CancellationToken ct); Task<Stream> FollowLogsAsync(string id, CancellationToken ct); }`; `internal sealed class DockerPipe : IDockerApi, IDisposable`; `internal enum DockerFailure { Down, Denied }`; `static DockerFailure DockerPipe.Classify(Exception e)`; `static string DockerPipe.PathFor(DockerPipe.Call call, string? id = null)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.RegularExpressions;
using Sanduhr.App.Desk;

namespace Sanduhr.App.Tests;

/// <summary>The rule that does not move (spec ci-watchers-3.9 section 2): Sanduhr lists containers and
/// follows a runner's log, and makes no other Docker call. Inspect returns a container's environment,
/// where a runner keeps its registration token.</summary>
public class DockerPipeBoundaryTests
{
    private static string AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private const string Id = "1111111111111111111111111111111111111111111111111111111111111111";

    [Fact]
    public void TheTwoCalls()
    {
        Assert.Equal("v1.43/containers/json", DockerPipe.PathFor(DockerPipe.Call.ListContainers));
        Assert.Equal($"v1.43/containers/{Id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200",
            DockerPipe.PathFor(DockerPipe.Call.FollowLogs, Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1111111111111111111111111111111111111111111111111111111111111111/json?")]
    [InlineData("../json")]
    public void ALogsCallTakesOnlyAFullContainerId(string? id) =>
        Assert.Throws<ArgumentException>(() => DockerPipe.PathFor(DockerPipe.Call.FollowLogs, id));

    [Fact]
    public void TheCallEnumHasExactlyTwoValues() =>
        Assert.Equal(["ListContainers", "FollowLogs"], Enum.GetNames<DockerPipe.Call>());

    [Fact]
    public void TheSourceMakesNoOtherCall()
    {
        var source = File.ReadAllText(Path.Combine(AppSource(), "Desk", "DockerPipe.cs"));
        Assert.Equal(2, Regex.Matches(source, "containers/").Count);
        Assert.DoesNotMatch(@"containers/\{[^}]*\}/json", source);
        foreach (var banned in new[] { "/exec", "/attach", "/archive", "/start", "/stop", "/kill", "/inspect", "/export",
                     "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync", "SendAsync", "HttpMethod." })
            Assert.DoesNotContain(banned, source);
    }

    [Fact]
    public void NothingElseInTheAppTalksToDocker()
    {
        var others = Directory.EnumerateFiles(AppSource(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) != "DockerPipe.cs")
            .Where(f => File.ReadAllText(f) is var s && (s.Contains("docker_engine") || s.Contains("containers/")))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(others);
    }

    [Fact]
    public void DeniedIsTheGroupProblemAndEverythingElseIsDown()
    {
        Assert.Equal(DockerFailure.Denied, DockerPipe.Classify(new HttpRequestException("x", new UnauthorizedAccessException())));
        Assert.Equal(DockerFailure.Denied, DockerPipe.Classify(new UnauthorizedAccessException()));
        Assert.Equal(DockerFailure.Down, DockerPipe.Classify(new HttpRequestException("x", new TimeoutException())));
        Assert.Equal(DockerFailure.Down, DockerPipe.Classify(new IOException()));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DockerPipeBoundaryTests`
Expected: build FAILS with `The type or namespace name 'DockerPipe' could not be found`.

- [ ] **Step 3: Implement `DockerPipe.cs`**

```csharp
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text.RegularExpressions;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

// DockerPipeBoundaryTests counts every "containers/" in this file: only PathFor may say it.

/// <summary>The two Docker calls CI watchers make (spec ci-watchers-3.9 section 2).</summary>
internal interface IDockerApi
{
    /// <summary>The list call: the body, at most <see cref="DockerContainers.MaxBytes"/>.</summary>
    Task<byte[]> ListContainersAsync(CancellationToken ct);

    /// <summary>A runner's log, followed, with timestamps and the last 200 lines first.</summary>
    Task<Stream> FollowLogsAsync(string id, CancellationToken ct);
}

internal enum DockerFailure { Down, Denied }

/// <summary>
/// Docker Engine's API over the local named pipe <c>\\.\pipe\docker_engine</c>, as HTTP/1.1 through an
/// <see cref="HttpClient"/> whose connection is the pipe (no network, no TCP). Exactly two calls exist,
/// <see cref="Call"/>; nothing builds a path from outside it. Never inspect: it returns a container's
/// environment, where a runner keeps its registration token. <c>DockerPipeBoundaryTests</c> pins this file.
/// </summary>
internal sealed partial class DockerPipe : IDockerApi, IDisposable
{
    public const string PipeName = "docker_engine";
    private const string ApiVersion = "v1.43";

    internal enum Call { ListContainers, FollowLogs }

    private readonly HttpClient _http;

    public DockerPipe()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (_, ct) =>
            {
                var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(2000, ct).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sanduhr");
    }

    internal static string PathFor(Call call, string? id = null) => call switch
    {
        Call.ListContainers => $"{ApiVersion}/containers/json",
        Call.FollowLogs when id is not null && IdPattern().IsMatch(id) =>
            $"{ApiVersion}/containers/{id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200",
        _ => throw new ArgumentException("Not a call Sanduhr makes to Docker.", nameof(call)),
    };

    public async Task<byte[]> ListContainersAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await _http.GetAsync(PathFor(Call.ListContainers), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await body.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
        {
            if (copy.Length + n > DockerContainers.MaxBytes) throw new IOException("Docker's container list is too large.");
            copy.Write(buffer, 0, n);
        }
        return copy.ToArray();
    }

    public async Task<Stream> FollowLogsAsync(string id, CancellationToken ct)
    {
        var response = await _http.GetAsync(PathFor(Call.FollowLogs, id), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Access denied anywhere in the chain is the docker-users group; everything else is Docker being down.</summary>
    internal static DockerFailure Classify(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
            if (x is UnauthorizedAccessException) return DockerFailure.Denied;
        return DockerFailure.Down;
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DockerPipeBoundaryTests`
Expected: PASS.

- [ ] **Step 5: Check the pipe on a machine with Docker (Neb)**

Run (PowerShell, from `windows-dotnet/`), a throwaway check that is not committed:

```powershell
docker ps --format "{{.Names}}" | Measure-Object | Select-Object -ExpandProperty Count
```

Expected: 7 on Neb (six runners and the egress proxy). Task 7's live test uses the same pipe.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/DockerPipe.cs windows-dotnet/tests/Sanduhr.App.Tests/DockerPipeBoundaryTests.cs
git commit -m "feat(ci-watchers): Docker over the local pipe, two calls only, pinned by a boundary test"
```

---

### Task 7: `RunnerWatchService` and the store hooks

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/RunnerWatchService.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskHandoffSeams.cs` (`WatcherStore`, near lines 89 to 260)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/RunnerWatchServiceTests.cs`

**Interfaces:**
- Consumes: `IDockerApi`, `DockerPipe.Classify`, `DockerFailure` (Task 6); `DockerContainers`, `RunnerStatus` (Task 4); `DockerLogFrames` (Task 3); `RunnerFollow` (Task 2); `RunnerLog.Parse` (Task 1).
- Produces: `internal sealed class RunnerWatchService(IDockerApi docker, Func<bool> enabled, IRunnerSink sink, Func<DateTimeOffset>? clock = null) : IDisposable` with `RunnerStatus Status`, `event Action? StatusChanged`, `void Refresh()`, `internal Task PollOnceAsync(CancellationToken ct)`, `internal Task WhenFollowsSettle()`, `static readonly TimeSpan ListEvery` (30 s), `RetryEvery` (60 s), `ReplayWait` (2 s); `internal interface IRunnerSink { void Apply(string runner, RunnerLine line); void Gone(string runner); void Clear(); }`; on `WatcherStore`: `ApplyRunner`, `RunnerGone`, `ClearRunners`, `event Action? FailedStarted`, implementing `IRunnerSink`.

The service raises sink calls on whatever thread its loop runs; `WatcherStore` marshals each call to the UI dispatcher (Step 5).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Buffers.Binary;
using System.Text;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The runner watch loop against a fake Docker (spec ci-watchers-3.9 sections 5 and 8).</summary>
public class RunnerWatchServiceTests
{
    private const string Id1 = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Id2 = "2222222222222222222222222222222222222222222222222222222222222222";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private static string ListJson(params (string Id, string Service, string Image)[] cs) =>
        "[" + string.Join(",", cs.Select(c =>
            $$"""{"Id":"{{c.Id}}","Names":["/x"],"Image":"{{c.Image}}","State":"running","Labels":{"com.docker.compose.service":"{{c.Service}}"}}""")) + "]";

    private static byte[] Frames(params string[] lines) => lines.SelectMany(l =>
    {
        var payload = Encoding.UTF8.GetBytes(l + "\n");
        var header = new byte[8];
        header[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)payload.Length);
        return header.Concat(payload);
    }).ToArray();

    private static string Line(DateTimeOffset at, string text) =>
        $"{at.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'} {at.UtcDateTime:yyyy-MM-dd HH:mm:ss'Z'}: {text}";

    private sealed class FakeDocker : IDockerApi
    {
        public Func<byte[]> List = () => Encoding.UTF8.GetBytes("[]");
        public Dictionary<string, Queue<byte[]>> Logs = new();
        public int Follows;
        public Task<byte[]> ListContainersAsync(CancellationToken ct) => Task.FromResult(List());
        public Task<Stream> FollowLogsAsync(string id, CancellationToken ct)
        {
            Follows++;
            var bytes = Logs.TryGetValue(id, out var q) && q.Count > 0 ? q.Dequeue() : [];
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }
    }

    private sealed class Sink : IRunnerSink
    {
        public readonly WatcherBoard Board = new();
        public int Glows, Clears;
        public void Apply(string runner, RunnerLine line) { lock (Board) if (Board.ApplyRunner(runner, line, Now).Failed) Glows++; }
        public void Gone(string runner) { lock (Board) Board.RunnerGone(runner, Now); }
        public void Clear() { lock (Board) { Board.Clear(WatcherSource.Runner); Clears++; } }
    }

    private static RunnerWatchService Service(FakeDocker d, Sink s, bool on = true) => new(d, () => on, s, () => Now);

    [Fact]
    public async Task ListsRunnersOnlyAndSaysSo()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "alpha-1", "dunder-gh-runner:2.337.0"), (Id2, "egress-proxy", "dunder-egress-proxy:1"))) };
        using var svc = Service(d, new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(new RunnerStatus(RunnerStatusKind.Watching, ["alpha-1"]), svc.Status);
        await svc.WhenFollowsSettle();
        Assert.Equal(1, d.Follows);
    }

    [Fact]
    public async Task NoRunners()
    {
        using var svc = Service(new FakeDocker(), new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(RunnerStatusKind.NoRunners, svc.Status.Kind);
    }

    [Fact]
    public async Task DockerDown()
    {
        var d = new FakeDocker { List = () => throw new HttpRequestException("x", new TimeoutException()) };
        using var svc = Service(d, new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(RunnerStatusKind.DockerDown, svc.Status.Kind);
    }

    [Fact]
    public async Task AccessDeniedShowsTheGroupLine()
    {
        var d = new FakeDocker { List = () => throw new HttpRequestException("x", new UnauthorizedAccessException()) };
        using var svc = Service(d, new Sink());
        var raised = 0;
        svc.StatusChanged += () => raised++;
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal("Docker refused the connection. Your Windows account needs to be in the docker-users group.", svc.Status.Text);
        Assert.Equal(1, raised); // once per change, not per retry
    }

    [Fact]
    public async Task ALiveJobBecomesACardAndAFailureGlows()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"))) };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build"), Line(Now.AddSeconds(5), "Job build completed with result: Failed"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Equal(WatcherState.Failed, sink.Board.Get("r:echo-1:build")!.State);
        Assert.Equal(1, sink.Glows);
    }

    [Fact]
    public async Task ReplayedFinishedJobsPutUpNothing()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"))) };
        d.Logs[Id1] = new([Frames(Line(Now.AddHours(-1), "Running job: build"), Line(Now.AddHours(-1).AddMinutes(1), "Job build completed with result: Succeeded"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Empty(sink.Board.Watchers);
    }

    [Fact]
    public async Task StreamEndRefollowsWithoutDuplicates()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"))) };
        var open = Frames(Line(Now.AddMinutes(-5), "Running job: build"));
        d.Logs[Id1] = new([open, open]); // the stream ends, the same tail comes back
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Single(sink.Board.Watchers);
        Assert.Equal(WatcherState.Running, sink.Board.Get("r:echo-1:build")!.State);
        Assert.Equal(2, d.Follows);
    }

    [Fact]
    public async Task ARunnerThatLeavesEndsItsCard()
    {
        var listed = true;
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(listed ? ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0")) : "[]") };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        listed = false;
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(WatcherState.Finished, sink.Board.Get("r:echo-1:build")!.State);
    }

    [Fact]
    public async Task SwitchingOffClearsAndSaysNothing()
    {
        var on = true;
        var sink = new Sink();
        using var svc = new RunnerWatchService(new FakeDocker(), () => on, sink, () => Now);
        await svc.PollOnceAsync(CancellationToken.None);
        on = false;
        svc.Refresh();
        Assert.Equal(RunnerStatus.Off, svc.Status);
        Assert.Equal(1, sink.Clears);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~RunnerWatchServiceTests`
Expected: build FAILS with `The type or namespace name 'RunnerWatchService' could not be found`.

- [ ] **Step 3: Implement `RunnerWatchService.cs`**

```csharp
using System.IO;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>Where runner lines go: the watcher store, which applies them on the UI thread.</summary>
internal interface IRunnerSink
{
    void Apply(string runner, RunnerLine line);
    void Gone(string runner);
    void Clear();
}

/// <summary>
/// CI watchers (spec ci-watchers-3.9 section 4): while "Show my CI runners' jobs" is on, lists Docker's
/// containers every <see cref="ListEvery"/>, follows each running runner's log on its own task, and hands
/// each line the replay gate lets through to the sink. Docker down or refusing is a status line, retried
/// every <see cref="RetryEvery"/>, never an error. Logs only state changes, never a job or runner name.
/// </summary>
internal sealed class RunnerWatchService(IDockerApi docker, Func<bool> enabled, IRunnerSink sink, Func<DateTimeOffset>? clock = null) : IDisposable
{
    public static readonly TimeSpan ListEvery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ReplayWait = TimeSpan.FromSeconds(2);

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private readonly object _gate = new();
    private readonly Dictionary<string, Follow> _follows = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loop;
    private bool _disposed;

    private sealed record Follow(string Runner, CancellationTokenSource Cts, Task Task);

    public RunnerStatus Status { get; private set; } = RunnerStatus.Off;

    /// <summary>The status line changed (raised on the loop's thread).</summary>
    public event Action? StatusChanged;

    /// <summary>The setting changed, or the app started: start or stop the loop.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        if (enabled())
        {
            if (_loop is not null) return;
            _loop = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_loop.Token));
            return;
        }
        _loop?.Cancel();
        _loop = null;
        StopAllFollows();
        sink.Clear();
        SetStatus(RunnerStatus.Off);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync(ct).ConfigureAwait(false);
            var wait = Status.Kind is RunnerStatusKind.DockerDown or RunnerStatusKind.DockerDenied ? RetryEvery : ListEvery;
            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One list: the status, a follow for each new runner, an end for each runner that left.</summary>
    internal async Task PollOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<DockerContainer> runners;
        try
        {
            runners = DockerContainers.Runners(DockerContainers.Parse(await docker.ListContainersAsync(ct).ConfigureAwait(false)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
        {
            StopAllFollows();
            SetStatus(new RunnerStatus(DockerPipe.Classify(e) == DockerFailure.Denied ? RunnerStatusKind.DockerDenied : RunnerStatusKind.DockerDown, []));
            return;
        }

        lock (_gate)
        {
            foreach (var (id, f) in _follows.Where(kv => runners.All(r => r.Id != kv.Key)).ToList())
            {
                f.Cts.Cancel();
                _follows.Remove(id);
                sink.Gone(f.Runner);
            }
            foreach (var r in runners)
            {
                if (_follows.TryGetValue(r.Id, out var existing) && !existing.Task.IsCompleted) continue;
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _follows[r.Id] = new Follow(r.Name, cts, FollowAsync(r.Id, r.Name, cts.Token));
            }
        }
        SetStatus(runners.Count == 0
            ? new RunnerStatus(RunnerStatusKind.NoRunners, [])
            : new RunnerStatus(RunnerStatusKind.Watching, runners.Select(r => r.Name).ToList()));
    }

    /// <summary>The tests' way to wait for the follows the last poll started (each fake stream ends).</summary>
    internal Task WhenFollowsSettle()
    {
        lock (_gate) return Task.WhenAll(_follows.Values.Select(f => f.Task));
    }

    private async Task FollowAsync(string id, string runner, CancellationToken ct)
    {
        try
        {
            await using var stream = await docker.FollowLogsAsync(id, ct).ConfigureAwait(false);
            var follow = new RunnerFollow(_clock());
            var frames = new DockerLogFrames();
            using var replayEnd = new CancellationTokenSource(ReplayWait);
            using var _ = replayEnd.Token.Register(() => Deliver(runner, follow.EndReplay));
            var buffer = new byte[16 * 1024];
            int n;
            while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                foreach (var text in frames.Push(buffer.AsSpan(0, n)))
                    if (RunnerLog.Parse(text) is { } line) Deliver(runner, () => follow.Feed(line));
            }
            Deliver(runner, follow.EndReplay); // a stream that ended before the wait ran out
        }
        catch (Exception e) when (e is OperationCanceledException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            // The next poll follows again if the runner is still there.
        }
    }

    /// <summary>Runs one step of the runner's <see cref="RunnerFollow"/> and applies what it lets through,
    /// all under the lock: the replay timer and the reader never touch the gate at once.</summary>
    private void Deliver(string runner, Func<IReadOnlyList<RunnerLine>> step)
    {
        lock (_gate)
            foreach (var line in step()) sink.Apply(runner, line);
    }

    private void StopAllFollows()
    {
        lock (_gate)
        {
            foreach (var f in _follows.Values) f.Cts.Cancel();
            _follows.Clear();
        }
    }

    private void SetStatus(RunnerStatus status)
    {
        if (status.Equals(Status)) return;
        if (status.Kind != Status.Kind)
            System.Diagnostics.Trace.WriteLine($"Sanduhr CI watchers: {status.Kind}");
        Status = status;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _loop?.Cancel();
        StopAllFollows();
    }
}
```

`RunnerFollow` is not thread-safe, so `Deliver` runs the gate step itself inside `_gate`: `EndReplay` from the timer and `Feed` from the reader never run at once or apply out of order. `EndReplay` returns nothing the second time, so the end-of-stream call after the timer fired is harmless.

Before using `System.Diagnostics.Trace`, check how this app logs: run `grep -rn "class .*Log\b\|static void Log(" windows-dotnet/src/Sanduhr.App/Services | head`. If an app logger exists (for example `AppLog.Info`), call it in `SetStatus` instead of `Trace.WriteLine`, with the same message.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~RunnerWatchServiceTests`
Expected: PASS.

- [ ] **Step 5: Make `WatcherStore` the sink**

In `DeskHandoffSeams.cs`, change the class line to `internal sealed class WatcherStore : IDisposable, IRunnerSink` and add, after the `WaitingStarted` event:

```csharp
    /// <summary>A runner's job just failed (the glow fires once, spec ci-watchers-3.9).</summary>
    public event Action? FailedStarted;

    // IRunnerSink: RunnerWatchService calls these from its own threads; each runs on the UI thread.
    void IRunnerSink.Apply(string runner, RunnerLine line) => OnUi(() => ApplyRunner(runner, line));
    void IRunnerSink.Gone(string runner) => OnUi(() => RunnerGone(runner));
    void IRunnerSink.Clear() => OnUi(ClearRunners);

    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) a();
        else d.BeginInvoke(a);
    }

    internal void ApplyRunner(string runner, RunnerLine line)
    {
        if (_disposed) return;
        var change = Board.ApplyRunner(runner, line, DateTimeOffset.Now);
        if (change.Changed) Changed?.Invoke();
        if (change.Failed) FailedStarted?.Invoke();
        Schedule();
    }

    internal void RunnerGone(string runner)
    {
        if (_disposed) return;
        if (Board.RunnerGone(runner, DateTimeOffset.Now)) Changed?.Invoke();
        Schedule();
    }

    internal void ClearRunners()
    {
        if (_disposed) return;
        Board.Clear(WatcherSource.Runner);
        Changed?.Invoke();
    }
```

Add `using Sanduhr.Core;` at the top of the file if it is not already there (it is, for `WatcherBoard`).

- [ ] **Step 6: Build and run the App tests**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~RunnerWatchServiceTests|FullyQualifiedName~DockerPipeBoundaryTests|FullyQualifiedName~CompanionPieceTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/RunnerWatchService.cs windows-dotnet/src/Sanduhr.App/Desk/DeskHandoffSeams.cs windows-dotnet/tests/Sanduhr.App.Tests/RunnerWatchServiceTests.cs
git commit -m "feat(ci-watchers): follow runner logs and feed the watcher board"
```

---

### Task 8: The setting, the status line and the wiring

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (after `WatchersSwitch`, near line 148)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (after `SaveWatchersAgents`, near line 839)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs` (the sorted key list, near line 107)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (after `WatchersAgents`, near line 279)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the `desk-watchers` panel, near line 2362)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (near line 556, and wherever `_watchers` is disposed and `WatchersChanged` is wired)

**Interfaces:**
- Consumes: `RunnerWatchService`, `DockerPipe`, `WatcherStore.FailedStarted` (Task 7); `RunnerStatus` (Task 4).
- Produces: `SettingsNames.CiRunnersSwitch`; `SettingsStore.LoadWatchersCiRunners()` / `SaveWatchersCiRunners(bool)`; `DeskSettingsViewModel.WatchersCiRunners`, `CiRunnersStatus`, `Action? CiRunnersChanged`; in `App`, `private event Action? RunnerStatusShown` (raised on the UI thread when the status changes).

- [ ] **Step 1: Write the failing contract test**

In `SettingsKeysContractTests.cs`, add `"watchers_ci_runners",` after `"watchers_agents",` in the sorted list.

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsKeysContractTests`
Expected: FAIL (the key is pinned but no code writes it).

- [ ] **Step 2: Add the name and the store methods**

`SettingsNames.cs`, after `WatchersSwitch`:

```csharp
    /// <summary>Desk: CI watchers from this PC's runner containers (settings.json "watchers_ci_runners", off by default; spec ci-watchers-3.9).</summary>
    public const string CiRunnersSwitch = "Show my CI runners' jobs";
```

`SettingsStore.cs`, after `SaveWatchersAgents`:

```csharp
    /// <summary>"Show my CI runners' jobs" (spec ci-watchers-3.9): <c>watchers_ci_runners</c>, absent = off.
    /// On, Sanduhr reads the log output of GitHub Actions runner containers through Docker's local pipe.</summary>
    public bool LoadWatchersCiRunners() => ReadBool(Read(), "watchers_ci_runners", false);

    public void SaveWatchersCiRunners(bool on)
    {
        var root = Read();
        if (on) root["watchers_ci_runners"] = true;
        else root.Remove("watchers_ci_runners");
        Write(root);
    }
```

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsKeysContractTests|FullyQualifiedName~SettingsNamesTests"`
Expected: PASS. If `SettingsNamesTests` pins the full list of names, add `CiRunnersSwitch` where it lists `WatchersSwitch`, following the same shape.

- [ ] **Step 3: Add the view model properties**

`DeskSettingsViewModel.cs`, after `WatchersAgents`:

```csharp
    /// <summary>Raised after "Show my CI runners' jobs" is saved (App starts or stops the runner watch).</summary>
    public Action? CiRunnersChanged { get; set; }

    /// <summary>"Show my CI runners' jobs" (spec ci-watchers-3.9): off by default; saved at once.</summary>
    public bool WatchersCiRunners
    {
        get => _store.LoadWatchersCiRunners();
        set
        {
            if (value == WatchersCiRunners) return;
            _store.SaveWatchersCiRunners(value);
            OnPropertyChanged();
            CiRunnersChanged?.Invoke();
        }
    }

    /// <summary>The line under the switch: which runners, or why none (a live region).</summary>
    [ObservableProperty] private string _ciRunnersStatus = "";
```

- [ ] **Step 4: Add the switch and the live status line**

`SettingsWindow.xaml`, inside the `desk-watchers` `StackPanel`, after the existing caption `TextBlock`:

```xml
                                <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,12,0,0"
                                          Content="{x:Static core:SettingsNames.CiRunnersSwitch}"
                                          IsChecked="{Binding WatchersCiRunners, Mode=TwoWay}"
                                          AutomationProperties.Name="{x:Static core:SettingsNames.CiRunnersSwitch}" />
                                <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                           Text="Off by default. Shows a card for each job a GitHub Actions runner container on this PC runs, however it was started. Sanduhr reads only the runners' log output through Docker's local pipe: no GitHub account, no network, and never a container's settings. Cards live in memory only." />
                                <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                           Text="{Binding CiRunnersStatus}"
                                           AutomationProperties.Name="CI runners status"
                                           AutomationProperties.LiveSetting="Polite" />
```

- [ ] **Step 5: Wire it in `App.xaml.cs`**

After `_watchers.Start();` (near line 562):

```csharp
        // CI watchers (spec ci-watchers-3.9): runner containers' jobs, only while "Show my CI runners' jobs" is on.
        _watchers.FailedStarted += () =>
        {
            if (_desk is { Running: true, WatchersPlaced: true }) _glow?.Preview(ClaudeCodeEvent.Waiting);
        };
        _dockerPipe = new Desk.DockerPipe();
        var runners = new Desk.RunnerWatchService(_dockerPipe, store.LoadWatchersCiRunners, _watchers);
        _runners = runners;
        runners.StatusChanged += () => Dispatcher.BeginInvoke(new Action(() => RunnerStatusShown?.Invoke()));
        runners.Refresh();
```

Add the fields and the event next to `private Desk.WatcherStore? _watchers;` (line 39):

```csharp
    private Desk.DockerPipe? _dockerPipe;
    private Desk.RunnerWatchService? _runners;

    /// <summary>The CI runners status changed (spec ci-watchers-3.9); raised on the UI thread for an open Settings.</summary>
    private event Action? RunnerStatusShown;
```

Settings builds a new view model each time it opens. Next to `svm.Desk.WatchersChanged = () => _watchers?.SwitchChanged();` (line 680), add:

```csharp
        svm.Desk.CiRunnersChanged = () => _runners?.Refresh();
        svm.Desk.CiRunnersStatus = _runners?.Status.Text ?? "";
        Action showRunnerStatus = () => svm.Desk.CiRunnersStatus = _runners?.Status.Text ?? "";
        RunnerStatusShown += showRunnerStatus;
```

and right after `_settingsWindow = new SettingsWindow(svm);` inside the `try` that follows it:

```csharp
            _settingsWindow.Closed += (_, _) => RunnerStatusShown -= showRunnerStatus;
```

`_watchers?.Dispose()` runs in three places in `App.xaml.cs` (near lines 955, 1059 and 1097). Before each, add:

```csharp
        _runners?.Dispose();
        _dockerPipe?.Dispose();
```

matching each site's indentation.

- [ ] **Step 6: Build and run the full suites**

Quit `Sanduhr.exe` if it is running, then:

Run: `dotnet build Sanduhr.slnx -c Debug` then `dotnet test tests/Sanduhr.Tests` then `dotnet test tests/Sanduhr.App.Tests` then `dotnet test tests/Sanduhr.Mcp.Tests`
Expected: build succeeds with no new warnings; all tests PASS, including `SettingsWindowNamesTests` (every Settings control has a name).

- [ ] **Step 7: Live check on Neb (Docker Desktop running)**

Run the debug build (`src/Sanduhr.App/bin/Debug/net10.0-windows*/Sanduhr.exe`), open Settings ▸ Desk, turn on "Show my CI runners' jobs".
Expected: within 30 s the status reads `Watching 6 runners: bravo-1, alpha-1, charlie-1, delta-1, echo-2, echo-1` (order as Docker lists them); no card appears (the runners are idle; the replay of alpha's 2026-10-22 jobs puts up nothing). Quit Docker Desktop: within 60 s the status reads `Docker isn't running.`. Turn the switch off: the status line empties.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs
git commit -m "feat(ci-watchers): Show my CI runners' jobs, with a live status line"
```

---

### Task 9: Docs, privacy and the reviewer letter

**Files:**
- Modify: `docs/PRIVACY.md` (a new row after "Watchers from Claude", line 59)
- Modify: `CHANGELOG.md` (a new `## Unreleased (windows)` section above `## v3.8.0`)
- Create: `docs/store/reviewer-letter-3.9.0.0.md`
- Modify: `windows-dotnet/README.md` (a "Since 3.8.0" section, following the file's existing pattern)
- Modify: `docs/ci-watchers-3.9-spec.md` (as-built notes)

- [ ] **Step 1: PRIVACY row**

Insert after the "Watchers from Claude" row:

```markdown
| **CI watchers** (Windows, 3.9; only while Settings ▸ Desk ▸ **Show my CI runners' jobs** is on, off by default) | Nothing on disk but the switch (`watchers_ci_runners` in `settings.json`) | Your Windows user account | Through Docker's local pipe (`\\.\pipe\docker_engine`), Sanduhr lists your containers (each one's id, image name, names, state and Compose service label) every 30 seconds and follows the log output of those whose image is a GitHub Actions runner (`actions-runner`, `github-runner` or `gh-runner` in the image's name). From each log it keeps only a job's name, its runner's name, when it started and ended, and its result, in memory until the job's card fades, you turn the switch off or you quit. It never reads any other container, any container's environment or settings (where a runner keeps its registration token), or files inside a container, and it never starts, stops or changes anything in Docker. No GitHub account, no token and no network: the pipe is local to your PC. Cards hide in demo mode. Only Docker's state (running, not running, refused) is logged, never a job or runner name. |
```

- [ ] **Step 2: CHANGELOG**

Above `## v3.8.0 — 2026-10-23`:

```markdown
## Unreleased (windows)

### CI watchers

- **Show my CI runners' jobs** (Settings ▸ Desk, off by default): a watcher card for every job a GitHub Actions runner container on this PC runs, however it was started. "Running job" puts it up, the result ends it: passed fades, failed stays and glows once, canceled or skipped fades as finished. The companion cheers a passed job.
- **Local only.** Sanduhr reads the runner containers' log output through Docker's local pipe: no GitHub account, no token, no network. It lists containers and follows logs, and makes no other Docker call; a test pins that, since a runner's settings hold its registration token. Runners are picked by image name, so runners added later show without setup.
- A status line under the switch names the runners it watches, or says Docker isn't running, or that your account needs the docker-users group.
- Runner cards never go "lost touch" (a runner is silent while a job runs), start with the job's real start time when Sanduhr opens mid-job, and hide in demo mode.
```

- [ ] **Step 3: Reviewer letter**

Create `docs/store/reviewer-letter-3.9.0.0.md` by copying `docs/store/reviewer-letter-3.8.0.0.md`, changing its version to 3.9.0.0, removing its 3.8-only paragraphs, and adding this paragraph where 3.8's new-feature paragraph sat:

```markdown
**New in 3.9.0.0: CI watchers (optional, off by default).** With Settings ▸ Desk ▸ "Show my CI runners' jobs" on, Sanduhr reads the log output of GitHub Actions runner containers on the user's own PC through Docker's local named pipe (`\\.\pipe\docker_engine`), to show job progress on the Desk. No network, no credentials, no new capability: a named-pipe client runs under `runFullTrust`, which the manifest already declares. Sanduhr makes two read-only Docker calls (list containers, follow a runner's log) and no others. Testing without Docker installed: the switch's status line reads "Docker isn't running." and nothing else changes.
```

Leave the file's remaining sections (trademark, privacy URL, test account) as 3.8.0.0 had them, with the version updated.

- [ ] **Step 4: README and spec as-built notes**

`windows-dotnet/README.md`, a new section at the end of the "Since …" sections:

```markdown
### Since 3.8.0: CI watchers from local runners

`docs/ci-watchers-3.9-spec.md`. Settings ▸ Desk ▸ **Show my CI runners' jobs** (`watchers_ci_runners`, off by default) puts a watcher card up for each job a GitHub Actions runner container on this PC runs. Core: `RunnerLog` (a log line to a start, end or restart), `RunnerFollow` (the replay gate), `DockerLogFrames` (Docker's multiplexed stream), `DockerContainers` (the list call, runner images, the status line), and `WatcherSource.Runner` on the board (no lost touch, hidden in demo mode). App: `DockerPipe` (an `HttpClient` over `\\.\pipe\docker_engine`, two calls only, pinned by `DockerPipeBoundaryTests`) and `RunnerWatchService`. Fixtures in `tests/Sanduhr.Tests/fixtures/runner-logs/` are real lines from Neb's runners plus derived ones, marked.
```

`docs/ci-watchers-3.9-spec.md`, apply these three as-built corrections:
- Section 4, `RunnerLog` bullets: `RunnerOnline` is `Listening for Jobs` only; move `Runner reconnected.` to the "nothing" list with the reason "it can come mid-job after a network blip".
- Section 4, `DockerLogFrames`: HTTP chunked encoding is `HttpClient`'s (its connection is the pipe, through `SocketsHttpHandler.ConnectCallback`); the class decodes frames only.
- Section 5, screen readers: the card reads "CI job test on alpha-1, running, 3m", the existing spoken-watcher shape.
- Section 7, the noise fixture: only `Listening for Jobs` is `RunnerOnline`.

- [ ] **Step 5: Commit**

```bash
git add docs/PRIVACY.md CHANGELOG.md docs/store/reviewer-letter-3.9.0.0.md windows-dotnet/README.md docs/ci-watchers-3.9-spec.md
git commit -m "docs(ci-watchers): PRIVACY row, changelog, 3.9.0.0 reviewer letter, as-built spec notes"
```

---

### Task 10: Smoke with a real job (needs the owner's nod)

Not code. Do this only after the owner says yes at that moment, because it changes a repo's settings for a few minutes.

- [ ] **Step 1:** Ask the owner which repo to route, and whether a deliberate failing run is fine (it gives a real `Failed` line to replace the derived one).
- [ ] **Step 2:** On the owner's yes, for the chosen repo: `gh variable set CI_RUNNER -R estevanhernandez-stack-ed/<repo> --body neb`, then re-run its latest workflow run: `gh run rerun <run-id> -R estevanhernandez-stack-ed/<repo>`.
- [ ] **Step 3:** With the debug build running and the switch on, watch the Desk: a card `<runner> · <job>` appears with the job's start, then ends with its result.
- [ ] **Step 4:** Remove the variable: `gh variable delete CI_RUNNER -R estevanhernandez-stack-ed/<repo>`. Confirm with `gh variable list -R estevanhernandez-stack-ed/<repo>`.
- [ ] **Step 5:** Copy any new real result lines (`docker logs --timestamps <container> | grep -E "Running job|completed with result"`) into a new real fixture file, and remove the matching derived lines. Run `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~RunnerLogTests`, then commit the fixture change.

What's New card and the 3.9.0 version lockstep are not in this plan: they belong to the 3.9 release paperwork (`docs/release-runbook.md`), once it is settled whether streamer mode ships in 3.9 too.
