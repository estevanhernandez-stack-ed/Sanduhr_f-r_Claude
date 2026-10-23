namespace Sanduhr.Core;

/// <summary>A runner's parsed log line applied to the watcher board (spec ci-watchers-3.9). Kept out of
/// Watchers.cs, which the MCP server source-links: the board's own runner operations take plain values.</summary>
public static class RunnerBoard
{
    public static RunnerChange ApplyRunner(this WatcherBoard board, string key, string runner, RunnerLine line, DateTimeOffset now) => line switch
    {
        RunnerLine.JobStarted s => board.RunnerStart(key, runner, s.Job, s.At, now),
        RunnerLine.JobEnded e => board.RunnerEnd(key, e.Job, e.Result, now),
        RunnerLine.Online => board.RunnerOnline(key, now),
        _ => default,
    };
}
