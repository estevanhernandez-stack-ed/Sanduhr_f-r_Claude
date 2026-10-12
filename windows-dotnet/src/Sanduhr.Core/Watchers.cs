using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

// Watchers: live cards for work in flight, ported from the Mac's Watchers.swift (the notch-only
// WatcherIntro is left out). An agent puts one up through the MCP tools' watch_start and moves it
// with watch_update and watch_end; Claude Code's own background work could show as one when the
// Stop hook hands Sanduhr the session's background tasks (the decoder is here, nothing calls it
// yet). Everything is pure and in memory: the board never touches a file, a log or the network.

/// <summary>Where a watcher is in its life.</summary>
public enum WatcherState
{
    Running,

    /// <summary>Waiting on you: it pulses and the glow fires once.</summary>
    Waiting,

    Passed,
    Failed,

    /// <summary>An automatic watcher whose task left the session's list: it ended, the result unknown.</summary>
    Finished,

    /// <summary>No update within its window: greyed until it hears again or is dismissed.</summary>
    LostTouch,
}

/// <summary>What a <see cref="WatcherState"/> means for ordering, fading and words.</summary>
public static class WatcherStates
{
    /// <summary>Lower is more urgent: waiting, then failed, then running, then the rest.</summary>
    public static int Urgency(this WatcherState s) => s switch
    {
        WatcherState.Waiting => 0,
        WatcherState.Failed => 1,
        WatcherState.Running => 2,
        WatcherState.LostTouch => 3,
        _ => 4,
    };

    public static bool IsEnded(this WatcherState s) =>
        s is WatcherState.Passed or WatcherState.Failed or WatcherState.Finished;

    /// <summary>Passed and finished fade out on their own; failed stays until dismissed.</summary>
    public static bool Fades(this WatcherState s) => s is WatcherState.Passed or WatcherState.Finished;

    /// <summary>The words a screen reader and the Desk's note line use.</summary>
    public static string Label(this WatcherState s) => s switch
    {
        WatcherState.Running => "running",
        WatcherState.Waiting => "waiting on you",
        WatcherState.Passed => "passed",
        WatcherState.Failed => "failed",
        WatcherState.Finished => "finished",
        _ => "lost touch",
    };

    /// <summary>The name in request and state files (the Mac's raw value: "lost_touch").</summary>
    public static string Wire(this WatcherState s) => s == WatcherState.LostTouch ? "lost_touch" : s.ToString().ToLowerInvariant();
}

/// <summary>Who put the watcher up.</summary>
public enum WatcherSource
{
    /// <summary>An agent, through the MCP tools.</summary>
    Agent,

    /// <summary>Claude Code's background work, through the Stop hook.</summary>
    Automatic,
}

/// <summary>One card. Immutable; the board replaces it on every change.</summary>
public sealed record Watcher
{
    /// <summary><c>a:&lt;id&gt;</c> for an agent's, <c>b:&lt;session&gt;:&lt;task id&gt;</c> for an automatic one.</summary>
    public required string Id { get; init; }

    public required WatcherSource Source { get; init; }
    public required string Title { get; init; }

    /// <summary>The resting form: the agent's short title, else derived (<see cref="WatcherText.ShortTitle"/>).</summary>
    public string Short { get; init; } = "";

    /// <summary>Opened on a click; https only (<see cref="WatcherLimits.Link"/>).</summary>
    public Uri? Link { get; init; }

    public int? Total { get; init; }
    public int? Done { get; init; }

    /// <summary>One line under the title.</summary>
    public string? Note { get; init; }

    public required WatcherState State { get; init; }
    public required DateTimeOffset Started { get; init; }

    /// <summary>The last time it heard anything: what lost touch is measured from.</summary>
    public required DateTimeOffset Touched { get; init; }

    public DateTimeOffset? Ended { get; init; }

    /// <summary>From a Claude Code folder linked to a work account, or flagged by the agent: hidden
    /// while demo mode is on.</summary>
    public bool Work { get; init; }

    /// <summary>The Claude Code session an automatic watcher belongs to.</summary>
    public string? Session { get; init; }

    /// <summary>An automatic watcher's kind of task (shell, subagent, monitor, workflow).</summary>
    public string? Kind { get; init; }

    /// <summary>How long it has run: until now, or until it ended.</summary>
    public TimeSpan Elapsed(DateTimeOffset now)
    {
        var span = (Ended ?? now) - Started;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }

    /// <summary>"3/10" when there is a total.</summary>
    public string? Progress => Total is { } total ? $"{Math.Min(Done ?? 0, total)}/{total}" : null;
}

/// <summary>The caps both sides apply (the MCP server first, the app again): one short line each.</summary>
public static class WatcherLimits
{
    public const int Title = 80;

    /// <summary>An agent's short title, for the resting line.</summary>
    public const int Short = 12;

    public const int Note = 140;
    public const int LinkLength = 2048;
    public const int Total = 1_000_000;

    /// <summary>One line of text: trimmed, control characters turned to spaces, at most
    /// <paramref name="cap"/> characters (longer is clipped with an ellipsis). Null when nothing is
    /// left. Characters are counted as the user sees them (text elements), as the Mac does.</summary>
    public static string? Line(string? s, int cap)
    {
        if (s is null) return null;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsControl(c) ? ' ' : c);
        var flat = sb.ToString().Trim();
        if (flat.Length == 0) return null;
        if (CountOf(flat) <= cap) return flat;
        return Prefix(flat, cap - 1).Trim() + "…";
    }

    /// <summary>An https link with a host and nothing else (no user, no password), null for anything else.</summary>
    public static Uri? Link(string? s)
    {
        if (s is null || s.Length > LinkLength) return null;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var url)) return null;
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrEmpty(url.Host) || url.UserInfo.Length > 0) return null;
        return url;
    }

    /// <summary>The first <paramref name="n"/> text elements of <paramref name="s"/>.</summary>
    internal static string Prefix(string s, int n)
    {
        if (n <= 0) return "";
        var e = StringInfo.GetTextElementEnumerator(s);
        var end = 0;
        var count = 0;
        while (count < n && e.MoveNext())
        {
            end = e.ElementIndex + e.GetTextElement().Length;
            count++;
        }
        return s[..end];
    }

    internal static int CountOf(string s) => new StringInfo(s).LengthInTextElements;
}

/// <summary>What an agent asks for, checked (<see cref="WatcherRequest"/> decodes it from the request file).</summary>
public abstract record WatcherCommand
{
    /// <summary><paramref name="Short"/> is the resting title; null derives one from the title.</summary>
    public sealed record Start(string Id, string Title, Uri? Link, int? Total, bool Work, string? Short = null) : WatcherCommand;

    /// <summary><paramref name="State"/> is running or waiting.</summary>
    public sealed record Update(string Id, int? Done, string? Note, WatcherState? State) : WatcherCommand;

    /// <summary><paramref name="Result"/> is passed or failed.</summary>
    public sealed record End(string Id, WatcherState Result, string? Note) : WatcherCommand;
}

/// <summary>One of Claude Code's background tasks as the Stop hook hands it over: never the shell
/// command, never the conversation.</summary>
public sealed record BackgroundTask(string Id, string Type, string Status, string Description, string? Name = null)
{
    /// <summary>Running and pending are in flight; failed and error ended badly; anything else that
    /// is not in flight (completed, killed, stopped) ended with the result unknown.</summary>
    public WatcherState State => Status.ToLowerInvariant() switch
    {
        "failed" or "error" => WatcherState.Failed,
        "completed" or "killed" or "stopped" or "cancelled" or "canceled" or "done" => WatcherState.Finished,
        _ => WatcherState.Running,
    };

    /// <summary>The card's title: the description, else the workflow's name, else the kind of task.</summary>
    public string Title =>
        WatcherLimits.Line(Description, WatcherLimits.Title)
        ?? WatcherLimits.Line(Name, WatcherLimits.Title)
        ?? $"Background {Kind}";

    /// <summary>The note line: what kind of work it is ("background shell", "workflow deploy").</summary>
    public string Note =>
        Type == "workflow" && WatcherLimits.Line(Name, 60) is { } name ? $"workflow {name}" : $"background {Kind}";

    /// <summary>The resting title: the workflow's name, else the description's first word.</summary>
    public string Short =>
        Type == "workflow" && WatcherLimits.Line(Name, 60) is { } name
            ? WatcherText.Clip(name, 10)
            : WatcherText.FirstWord(Description) ?? WatcherText.Clip(Kind, 10);

    public string Kind => WatcherLimits.Line(Type.ToLowerInvariant(), 20) ?? "task";
}

/// <summary>One Stop's report: the session and its in-flight background work.</summary>
public sealed record StopReport(string Session, string? Folder, IReadOnlyList<BackgroundTask> Tasks);

/// <summary>Every watcher, and how they change (the Mac's <c>WatcherBoard</c>). Pure: the time comes in.</summary>
public sealed class WatcherBoard
{
    /// <summary>An agent's watcher with no update for this long has lost touch.</summary>
    public static readonly TimeSpan AgentWindow = TimeSpan.FromMinutes(10);

    /// <summary>An automatic watcher is confirmed or ended at each Stop of its session. One that
    /// hears no Stop for this long (the session was closed mid-task) has lost touch.</summary>
    public static readonly TimeSpan AutomaticWindow = TimeSpan.FromHours(1);

    /// <summary>Passed and finished fade after this long.</summary>
    public static readonly TimeSpan FadeAfter = TimeSpan.FromSeconds(6);

    /// <summary>Most watchers kept; past it the least urgent, oldest go.</summary>
    public const int Capacity = 24;

    /// <summary>Most a session's Stop may bring.</summary>
    public const int TasksPerStop = 20;

    private List<Watcher> _watchers = [];

    public IReadOnlyList<Watcher> Watchers => _watchers;

    public static TimeSpan Window(WatcherSource source) => source == WatcherSource.Agent ? AgentWindow : AutomaticWindow;

    public static string AgentId(string id) => $"a:{id}";

    public static string AutomaticId(string session, string task) => $"b:{session}:{task}";

    public Watcher? Get(string id) => _watchers.FirstOrDefault(w => w.Id == id);

    /// <summary>Applies an agent's command. True when a watcher started waiting on you (the glow
    /// fires once). Updating or ending a watcher that isn't there does nothing.</summary>
    public bool Apply(WatcherCommand command, DateTimeOffset now)
    {
        switch (command)
        {
            case WatcherCommand.Start s:
            {
                var key = AgentId(s.Id);
                _watchers.RemoveAll(w => w.Id == key);
                _watchers.Add(new Watcher
                {
                    Id = key, Source = WatcherSource.Agent, Title = s.Title,
                    Short = s.Short ?? WatcherText.ShortTitle(s.Title), Link = s.Link, Total = s.Total,
                    Done = s.Total is null ? null : 0, State = WatcherState.Running,
                    Started = now, Touched = now, Work = s.Work,
                });
                Trim();
                return false;
            }
            case WatcherCommand.Update u:
            {
                var i = _watchers.FindIndex(w => w.Id == AgentId(u.Id));
                if (i < 0 || _watchers[i].State.IsEnded()) return false;
                var w = _watchers[i];
                var was = w.State;
                _watchers[i] = w with
                {
                    Done = u.Done is { } d ? Math.Max(0, d) : w.Done,
                    Note = u.Note ?? w.Note,
                    State = u.State ?? (was == WatcherState.LostTouch ? WatcherState.Running : was),
                    Touched = now,
                };
                return _watchers[i].State == WatcherState.Waiting && was != WatcherState.Waiting;
            }
            case WatcherCommand.End e:
            {
                var i = _watchers.FindIndex(w => w.Id == AgentId(e.Id));
                if (i < 0 || _watchers[i].State.IsEnded()) return false;
                var w = _watchers[i];
                _watchers[i] = w with
                {
                    Note = e.Note ?? w.Note,
                    Done = e.Result == WatcherState.Passed && w.Total is { } total ? total : w.Done,
                    State = e.Result,
                    Touched = now,
                    Ended = now,
                };
                return false;
            }
            default:
                return false;
        }
    }

    /// <summary>A Stop of <c>report.Session</c>: each task in flight is a watcher (added, or touched
    /// and updated), and the session's automatic watchers whose task left the list end as finished.
    /// A task the list reports as ended ends with it. True when anything changed.</summary>
    public bool ApplyStop(StopReport report, bool work, DateTimeOffset now)
    {
        var before = _watchers.ToList();
        var seen = new HashSet<string>();
        foreach (var task in report.Tasks.Take(TasksPerStop))
        {
            var key = AutomaticId(report.Session, task.Id);
            if (!seen.Add(key)) continue;
            var state = task.State;
            var i = _watchers.FindIndex(w => w.Id == key);
            if (i >= 0)
            {
                if (_watchers[i].State.IsEnded()) continue;
                _watchers[i] = _watchers[i] with
                {
                    Title = task.Title, Short = task.Short, Note = task.Note, Kind = task.Kind,
                    Touched = now, State = state, Ended = state.IsEnded() ? now : _watchers[i].Ended,
                };
            }
            else if (!state.IsEnded())
            {
                _watchers.Add(new Watcher
                {
                    Id = key, Source = WatcherSource.Automatic, Title = task.Title, Short = task.Short,
                    Note = task.Note, State = WatcherState.Running, Started = now, Touched = now,
                    Work = work, Session = report.Session, Kind = task.Kind,
                });
            }
        }
        for (var i = 0; i < _watchers.Count; i++)
        {
            var w = _watchers[i];
            if (w.Source == WatcherSource.Automatic && w.Session == report.Session
                && !seen.Contains(w.Id) && !w.State.IsEnded())
            {
                _watchers[i] = w with { State = WatcherState.Finished, Touched = now, Ended = now };
            }
        }
        Trim();
        return !_watchers.SequenceEqual(before);
    }

    /// <summary>The clock moved: running and waiting watchers past their window lose touch, passed
    /// and finished ones past the fade go. True when anything changed.</summary>
    public bool Tick(DateTimeOffset now)
    {
        var before = _watchers.ToList();
        _watchers.RemoveAll(w => w.State.Fades() && w.Ended is { } ended && now - ended >= FadeAfter);
        for (var i = 0; i < _watchers.Count; i++)
        {
            var w = _watchers[i];
            if (w.State is WatcherState.Running or WatcherState.Waiting && now - w.Touched > Window(w.Source))
                _watchers[i] = w with { State = WatcherState.LostTouch };
        }
        return !_watchers.SequenceEqual(before);
    }

    /// <summary>The next moment <see cref="Tick"/> would change something, null when nothing waits on the clock.</summary>
    public DateTimeOffset? NextChange()
    {
        DateTimeOffset? best = null;
        foreach (var w in _watchers)
        {
            DateTimeOffset? at = null;
            if (w.State.Fades() && w.Ended is { } ended) at = ended + FadeAfter;
            else if (w.State is WatcherState.Running or WatcherState.Waiting)
                at = w.Touched + Window(w.Source) + TimeSpan.FromSeconds(1);
            if (at is { } a && (best is null || a < best)) best = a;
        }
        return best;
    }

    public void Dismiss(string id) => _watchers.RemoveAll(w => w.Id == id);

    public void DismissAll() => _watchers.Clear();

    /// <summary>Drops every watcher from <paramref name="source"/> (its switch went off).</summary>
    public void Clear(WatcherSource source) => _watchers.RemoveAll(w => w.Source == source);

    /// <summary>Most urgent first (waiting, failed, running, lost touch, then passed and finished),
    /// then the most recently heard from.</summary>
    public IReadOnlyList<Watcher> Ordered() =>
        _watchers
            .OrderBy(w => w.State.Urgency())
            .ThenByDescending(w => w.Touched)
            .ThenBy(w => w.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>What the Desk shows: the ordered watchers, less the work ones in demo mode.</summary>
    public static IReadOnlyList<Watcher> Shown(IReadOnlyList<Watcher> ordered, bool demo) =>
        demo ? ordered.Where(w => !w.Work).ToList() : ordered;

    /// <summary>Past capacity, the least urgent and oldest go first.</summary>
    private void Trim()
    {
        if (_watchers.Count <= Capacity) return;
        var keep = Ordered().Take(Capacity).Select(w => w.Id).ToHashSet();
        _watchers.RemoveAll(w => !keep.Contains(w.Id));
    }
}

/// <summary>The words the Desk draws.</summary>
public static partial class WatcherText
{
    /// <summary>"12s", "4m", "1h 05m".</summary>
    public static string Elapsed(TimeSpan span)
    {
        var s = (int)Math.Floor(span.TotalSeconds);
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m";
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600}h {s % 3600 / 60:00}m");
    }

    /// <summary>The resting line: "PR 140 · 4/12", else "deploy · 4m" without a total, plus " +N"
    /// for the other watchers. Null with no watcher.</summary>
    public static string? RestLine(IReadOnlyList<Watcher> shown, DateTimeOffset now)
    {
        if (shown.Count == 0) return null;
        var top = shown[0];
        var short_ = top.Short.Length == 0 ? ShortTitle(top.Title) : top.Short;
        var line = $"{short_} · {top.Progress ?? Elapsed(top.Elapsed(now))}";
        return shown.Count > 1 ? $"{line} +{shown.Count - 1}" : line;
    }

    /// <summary>The full line: the whole title, the time so far, the progress and the count.</summary>
    public static string? FullLine(IReadOnlyList<Watcher> shown, DateTimeOffset now)
    {
        if (shown.Count == 0) return null;
        var top = shown[0];
        var parts = new List<string> { top.Title, Elapsed(top.Elapsed(now)) };
        if (top.Progress is { } p) parts.Add(p);
        var line = string.Join(" · ", parts);
        return shown.Count > 1 ? $"{line} +{shown.Count - 1}" : line;
    }

    /// <summary>A short title from a long one: "PR 140" (from "PR 140", "PR #140", "pull 140"), else
    /// "#140" (a number anywhere), else "v2.8.0" ("Release 2.8.0", "v2.8.0"), else the first word,
    /// clipped to 10 characters.</summary>
    public static string ShortTitle(string title)
    {
        if (PullPattern().Match(title) is { Success: true } pr) return $"PR {pr.Groups[1].Value}";
        if (HashPattern().Match(title) is { Success: true } hash) return $"#{hash.Groups[1].Value}";
        var version = ReleasePattern().Match(title);
        if (!version.Success) version = VersionPattern().Match(title);
        if (version.Success) return $"v{version.Groups[1].Value}";
        return FirstWord(title) ?? Clip(title, 10);
    }

    /// <summary>The first word, without trailing punctuation, clipped to 10 characters.</summary>
    public static string? FirstWord(string s)
    {
        var word = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (word is null) return null;
        var trimmed = word.Trim(':', ';', ',', '.', '!', '?', '-', '–', '—');
        return trimmed.Length == 0 ? null : Clip(trimmed, 10);
    }

    public static string Clip(string s, int n) => WatcherLimits.Prefix(s, n);

    /// <summary>The Desk row's second line: the note, else the state when it says something the dot
    /// doesn't ("waiting on you", "lost touch").</summary>
    public static string? DeskNote(Watcher w)
    {
        if (w.Note is { } note) return note;
        return w.State is WatcherState.Waiting or WatcherState.LostTouch or WatcherState.Failed or WatcherState.Finished
            ? w.State.Label()
            : null;
    }

    /// <summary>What a screen reader reads for a watcher.</summary>
    public static string Spoken(Watcher w, DateTimeOffset now)
    {
        var parts = new List<string> { w.Title, w.State.Label(), Elapsed(w.Elapsed(now)) };
        if (w.Progress is { } p) parts.Add(p);
        if (w.Note is { } note) parts.Add(note);
        return string.Join(", ", parts);
    }

    [GeneratedRegex(@"\b(?:PR|pull)\s*#?\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PullPattern();

    [GeneratedRegex(@"#(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    [GeneratedRegex(@"\brelease\s+v?(\d+(?:\.\d+)+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleasePattern();

    [GeneratedRegex(@"(?:^|[^\w])v(\d+(?:\.\d+)+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
