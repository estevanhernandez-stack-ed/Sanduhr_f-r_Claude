namespace Sanduhr.Core;

// The turn-card half of the board. Kept out of Watchers.cs because the MCP server links that file and must not
// link AgentTurns.cs (its allowlist is a design decision).

public sealed partial class WatcherBoard
{
    /// <summary>An agent's turn moved (5.0 spec 4). Started puts up a running card; waiting marks it; finished and stopped
    /// end it, showing the turn's own length. A card that is not there is created, so a turn already running when Sanduhr
    /// started still shows its end. True when the board changed.</summary>
    public bool ApplyTurn(AgentTurnEvent e, string title, string shortTitle, DateTimeOffset now)
    {
        if (e.Kind == AgentTurnKind.Idle) return false;   // the idle reminder is never a card (the rules never send it here)
        var before = _watchers.ToList();
        var id = TurnId(e.Agent, e.Session);
        var i = _watchers.FindIndex(w => w.Id == id);
        var old = i >= 0 ? _watchers[i] : null;

        Watcher card = e.Kind switch
        {
            AgentTurnKind.Started => Turn(id, e, title, shortTitle, WatcherState.Running, now, now, null, null),
            AgentTurnKind.Waiting => Turn(id, e, title, shortTitle, WatcherState.Waiting, old?.Started ?? now, now, null, null) with
            {
                Folder = e.Folder ?? old?.Folder,
            },
            _ => Turn(id, e, title, shortTitle, WatcherState.Finished,
                StartOf(e.Duration, now) ?? old?.Started ?? now, now, now,
                e.Kind == AgentTurnKind.Stopped ? "stopped" : null),
        };
        if (i >= 0) _watchers[i] = card;
        else _watchers.Add(card);
        Trim();
        return !_watchers.SequenceEqual(before);
    }

    private static Watcher Turn(string id, AgentTurnEvent e, string title, string shortTitle, WatcherState state,
        DateTimeOffset started, DateTimeOffset touched, DateTimeOffset? ended, string? note) => new()
    {
        Id = id, Source = WatcherSource.Turn, Title = Clip(title), Short = shortTitle, Note = note, State = state,
        Started = started, Touched = touched, Ended = ended, Session = e.Session, Kind = e.Agent, Folder = e.Folder,
    };

    /// <summary>Rebuilds each turn card's title (streamer mode changed). True when one changed.</summary>
    public bool RetitleTurns(Func<Watcher, (string Title, string Short)> name)
    {
        bool changed = false;
        for (var i = 0; i < _watchers.Count; i++)
        {
            var w = _watchers[i];
            if (w.Source != WatcherSource.Turn) continue;
            var (raw, shortTitle) = name(w);
            var title = Clip(raw);
            if (title == w.Title && shortTitle == w.Short) continue;
            _watchers[i] = w with { Title = title, Short = shortTitle };
            changed = true;
        }
        return changed;
    }

    /// <summary>When a turn of <paramref name="duration"/> ending <paramref name="now"/> started; null for no duration, a
    /// negative one, or one longer than the calendar reaches back (<c>now - d</c> would throw, also through a negative offset).</summary>
    private static DateTimeOffset? StartOf(TimeSpan? duration, DateTimeOffset now)
    {
        if (duration is not { } d || d < TimeSpan.Zero || d > now - DateTimeOffset.MinValue) return null;
        try { return now - d; }
        catch (ArgumentOutOfRangeException) { return null; }   // within a day of the start of the calendar, in a negative offset
    }

    /// <summary>One line, at most <see cref="WatcherLimits.Title"/> long, as other cards are.</summary>
    private static string Clip(string title) => WatcherLimits.Line(title, WatcherLimits.Title) ?? "";

    /// <summary>The agent wrote a line for an open turn: its card has heard from it. A running or waiting card is touched; one
    /// that lost touch is running again. A card that is not there, or has ended, is left alone. True when the board changed.</summary>
    public bool TouchTurn(string agent, string session, DateTimeOffset now)
    {
        var i = _watchers.FindIndex(w => w.Id == TurnId(agent, session));
        if (i < 0) return false;
        var w = _watchers[i];
        if (w.State is not (WatcherState.Running or WatcherState.Waiting or WatcherState.LostTouch)) return false;
        var touched = w.State == WatcherState.LostTouch
            ? w with { State = WatcherState.Running, Touched = now, Ended = null }
            : w with { Touched = now };
        if (touched == w) return false;
        _watchers[i] = touched;
        return true;
    }

    /// <summary>Drops one agent's turn cards (its source or its notifications went off). True when a card went.</summary>
    public bool ClearTurns(string agent) =>
        _watchers.RemoveAll(w => w.Source == WatcherSource.Turn && w.Kind == agent) > 0;
}
