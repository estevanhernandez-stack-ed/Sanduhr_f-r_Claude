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
        var change = b.ApplyRunner("alpha-1", "alpha-1", Start("test (ubuntu-latest, 3.12)", at: -30), T0);
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
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
        Assert.Equal(new RunnerChange(true, failed), b.ApplyRunner("echo-1", "echo-1", End("build", result), T0.AddSeconds(60)));
        var w = b.Get("r:echo-1:build")!;
        Assert.Equal(result, w.State);
        Assert.Equal(T0.AddSeconds(60), w.Ended);
    }

    [Fact]
    public void AnEndWithNoCardDoesNothing()
    {
        var b = new WatcherBoard();
        Assert.Equal(new RunnerChange(false, false), b.ApplyRunner("echo-1", "echo-1", End("build", WatcherState.Failed), T0));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void ASecondEndDoesNotGlowAgain()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
        b.ApplyRunner("echo-1", "echo-1", End("build", WatcherState.Failed), T0);
        Assert.Equal(new RunnerChange(false, false), b.ApplyRunner("echo-1", "echo-1", End("build", WatcherState.Failed), T0));
    }

    [Fact]
    public void ANewJobOnTheSameRunnerEndsTheOpenOneAsFinished()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
        b.ApplyRunner("echo-1", "echo-1", Start("test", at: 30), T0.AddSeconds(30));
        Assert.Equal(WatcherState.Finished, b.Get("r:echo-1:build")!.State);
        Assert.Equal(WatcherState.Running, b.Get("r:echo-1:test")!.State);
    }

    [Fact]
    public void OtherRunnersAreUntouched()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
        b.ApplyRunner("echo-2", "echo-2", Start("build"), T0);
        b.ApplyRunner("echo-2", "echo-2", new RunnerLine.Online(T0), T0);
        Assert.Equal(WatcherState.Running, b.Get("r:echo-1:build")!.State);
        Assert.Equal(WatcherState.Finished, b.Get("r:echo-2:build")!.State);
    }

    [Fact]
    public void SiblingsThatShareALabelAreToldApartByTheirKey()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("ci-runner-1", "runner", Start("build"), T0);
        b.ApplyRunner("ci-runner-2", "runner", Start("test", at: 10), T0.AddSeconds(10));
        // B starts a new job and comes online again: A's running card is untouched.
        b.ApplyRunner("ci-runner-2", "runner", Start("lint", at: 20), T0.AddSeconds(20));
        b.ApplyRunner("ci-runner-2", "runner", new RunnerLine.Online(T0), T0.AddSeconds(30));
        Assert.Equal(WatcherState.Running, b.Get("r:ci-runner-1:build")!.State);
        Assert.Equal(WatcherState.Finished, b.Get("r:ci-runner-2:test")!.State);
        // B ends its job: only B's card moves.
        b.ApplyRunner("ci-runner-2", "runner", Start("lint", at: 40), T0.AddSeconds(40));
        b.ApplyRunner("ci-runner-2", "runner", End("lint", WatcherState.Failed), T0.AddSeconds(50));
        Assert.Equal(WatcherState.Running, b.Get("r:ci-runner-1:build")!.State);
        Assert.Equal(WatcherState.Failed, b.Get("r:ci-runner-2:lint")!.State);
        Assert.Equal("runner · build", b.Get("r:ci-runner-1:build")!.Title);
    }

    [Fact]
    public void ASiblingLeavingEndsOnlyItsOwnCard()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("ci-runner-1", "runner", Start("build"), T0);
        b.ApplyRunner("ci-runner-2", "runner", Start("test"), T0);
        Assert.True(b.RunnerGone("ci-runner-2", T0.AddSeconds(5)));
        Assert.Equal(WatcherState.Running, b.Get("r:ci-runner-1:build")!.State);
        Assert.Equal(WatcherState.Finished, b.Get("r:ci-runner-2:test")!.State);
    }

    [Fact]
    public void ALongRunnerAndJobAreClippedToTheTitleCap()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("k", new string('r', 40), Start(new string('j', 120)), T0);
        Assert.True(b.Watchers.Single().Title.Length <= WatcherLimits.Title);
    }

    [Fact]
    public void AnEndedRunnerCardSaysSoOnTheDesk()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("k", "k", Start("build"), T0);
        Assert.Equal("CI runner", WatcherText.DeskNote(b.Get("r:k:build")!));
        b.ApplyRunner("k", "k", End("build", WatcherState.Failed), T0);
        Assert.Equal("failed", WatcherText.DeskNote(b.Get("r:k:build")!));
    }

    [Fact]
    public void RunnerGoneEndsItsOpenCard()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
        Assert.True(b.RunnerGone("echo-1", T0.AddSeconds(5)));
        Assert.Equal(WatcherState.Finished, b.Get("r:echo-1:build")!.State);
        Assert.False(b.RunnerGone("echo-1", T0.AddSeconds(6)));
    }

    [Fact]
    public void RunnerCardNeverLosesTouch_AndSchedulesNothing()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
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
        b.ApplyRunner("echo-1", "echo-1", Start("build"), T0);
        b.ApplyRunner("echo-1", "echo-1", End("build", WatcherState.Passed), T0);
        Assert.Equal(T0 + WatcherBoard.FadeAfter, b.NextChange());
        Assert.True(b.Tick(T0 + WatcherBoard.FadeAfter));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void DemoModeHidesRunnerCards()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("alpha-1", "alpha-1", Start("build"), T0);
        b.Apply(new WatcherCommand.Start("w1", "deploy", null, null, false), T0);
        Assert.Equal(["a:w1"], WatcherBoard.Shown(b.Ordered(), demo: true).Select(w => w.Id));
        Assert.Equal(2, WatcherBoard.Shown(b.Ordered(), demo: false).Count);
    }

    [Fact]
    public void ClearDropsOnlyRunnerCards()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("alpha-1", "alpha-1", Start("build"), T0);
        b.Apply(new WatcherCommand.Start("w1", "deploy", null, null, false), T0);
        b.Clear(WatcherSource.Runner);
        Assert.Equal([WatcherSource.Agent], b.Watchers.Select(w => w.Source));
    }

    [Fact]
    public void SpokenNamesTheJobAndRunner()
    {
        var b = new WatcherBoard();
        b.ApplyRunner("alpha-1", "alpha-1", Start("test"), T0);
        Assert.Equal("CI job test on alpha-1, running, 3m", WatcherText.Spoken(b.Get("r:alpha-1:test")!, T0.AddMinutes(3)));
    }
}
