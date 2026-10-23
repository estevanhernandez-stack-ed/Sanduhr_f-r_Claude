namespace Sanduhr.Core;

/// <summary>
/// One runner's log, followed (spec ci-watchers-3.9 section 5). Docker sends the last lines first;
/// lines stamped more than <see cref="ReplayMargin"/> before the follow opened are that replay. They
/// only rebuild which job is open: a job that ended in the replay puts up no card, and a job still
/// open when the replay ends comes out once, with its real start. The replay ends at the first live
/// line or when the caller says so (<see cref="EndReplay"/>, a moment after the stream opened, for an
/// idle runner that prints nothing new). When the replay ends with no job open the runner is idle, so
/// <see cref="EndReplay"/> says so with an <see cref="RunnerLine.Online"/>: a card left open by an earlier
/// follow (the stream dropped mid-job and the job ended meanwhile) then ends instead of staying Running.
/// Not thread-safe: the caller serializes <see cref="Feed"/> and <see cref="EndReplay"/>.
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

    /// <summary>Ends the replay: the job still open, if any, once; with none open, the runner is idle
    /// (<see cref="RunnerLine.Online"/>, stamped when the follow opened). Empty on every later call.</summary>
    public IReadOnlyList<RunnerLine> EndReplay()
    {
        if (Live) return [];
        Live = true;
        var open = _open;
        _open = null;
        return open is null ? [new RunnerLine.Online(opened)] : [open];
    }
}
