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
