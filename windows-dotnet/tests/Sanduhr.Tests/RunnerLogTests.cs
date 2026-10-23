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
    public void RealCanceledRun()
    {
        var parsed = Fixture("neb-alpha-canceled.log").Select(RunnerLog.Parse).ToList();
        Assert.Equal(
            new RunnerLine?[]
            {
                new RunnerLine.JobStarted("test", Utc(21, 37, 12, day: 23)),
                new RunnerLine.JobEnded("test", WatcherState.Finished, Utc(21, 37, 36, day: 23)),
            },
            parsed);
    }

    [Fact]
    public void DerivedLinesParse()
    {
        var ends = Fixture("derived.log").Select(RunnerLog.Parse).OfType<RunnerLine.JobEnded>()
            .Select(e => (e.Job, e.Result)).ToList();
        Assert.Equal(
            new[]
            {
                ("build", WatcherState.Failed), ("docs", WatcherState.Passed),
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
