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
        Assert.Equal(new RunnerLine[] { new RunnerLine.Online(Opened) }, f.EndReplay()); // idle, no card
        Assert.True(f.Live);
        Assert.Empty(f.EndReplay()); // once
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
        Assert.Equal(new RunnerLine[] { new RunnerLine.Online(Opened) }, f.EndReplay());
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
        Assert.Equal(new RunnerLine[] { new RunnerLine.Online(Opened), start }, f.Feed(start));
    }

    [Fact]
    public void AfterReplayEveryLinePassesThrough()
    {
        var f = new RunnerFollow(Opened);
        Assert.Equal(new RunnerLine[] { new RunnerLine.Online(Opened) }, f.EndReplay());
        var old = End("x", 3600);
        Assert.Equal(new RunnerLine[] { old }, f.Feed(old));
    }
}
