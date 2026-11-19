using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The file Claude Code's hook writes for each event (5.0 spec 4): <c>cc-signal-&lt;unix ms&gt;-&lt;pid&gt;.json</c> under
/// <c>%USERPROFILE%\.sanduhr</c>, holding the event, the session's folder and its id. One file per event, so two sessions
/// never overwrite each other. Sanduhr deletes each as it reads it. Nothing of the conversation is in it.
/// </summary>
public static class ClaudeCodeSignalFile
{
    public const string Prefix = "cc-signal-";
    public const string Pattern = Prefix + "*.json";
    /// <summary>A file the hook wrote and did not rename (a failed move): Reset and the watcher clear these.</summary>
    public const string TempPattern = Prefix + "*.json.tmp";
    /// <summary>The hook writes nothing while this many signals already wait unread (Sanduhr not running, or gone).</summary>
    public const int MaxWaiting = 100;
    public const int MaxBytes = 4096;
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxAhead = TimeSpan.FromMinutes(1);

    public const string Prompt = "prompt";
    public const string Waiting = "waiting";
    public const string Done = "done";

    /// <summary>The session of a signal that named none.</summary>
    public const string NoSession = "-";

    public sealed record Signal(string Event, string Session, string? Folder, DateTimeOffset At);

    /// <summary>The signal in <paramref name="data"/>, null for anything that is not one: over the size cap, not this
    /// version, an unknown event, older than <see cref="MaxAge"/> or stamped in the future.</summary>
    public static Signal? Decode(ReadOnlySpan<byte> data, DateTimeOffset now)
    {
        if (data.Length == 0 || data.Length > MaxBytes) return null;
        try
        {
            if (JsonNode.Parse(data) is not JsonObject o) return null;
            if (o["v"] is not JsonValue v || !v.TryGetValue<int>(out var version) || version != 1) return null;
            var ev = Str(o, "event");
            if (ev != Prompt && ev != Waiting && ev != Done) return null;
            if (o["at"] is not JsonValue a || !a.TryGetValue<long>(out var ms)) return null;
            var at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            if (now - at > MaxAge || at - now > MaxAhead) return null;

            var session = Str(o, "session");
            if (string.IsNullOrWhiteSpace(session)) session = NoSession;
            if (session.Length > 128) return null;
            var folder = Str(o, "cwd");
            if (string.IsNullOrWhiteSpace(folder) || folder.Length > 1024) folder = null;
            return new Signal(ev!, session, folder, at);
        }
        // Duplicate keys throw ArgumentException lazily; a time out of range throws ArgumentOutOfRangeException (an ArgumentException).
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException) { return null; }
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>Turns Claude Code's signals into turn events: a prompt starts a turn, done finishes it with the time between
/// the two, waiting inside an open turn is waiting. A waiting with no open turn is <see cref="AgentTurnKind.Idle"/>: the
/// hook's Notification matcher also catches Claude Code's idle reminder, raised a minute after a turn ends, and that is not
/// a wait on you, so it has its own switch. Start times are kept per session, in memory, for at most
/// <see cref="MaxSessions"/>.</summary>
public sealed class ClaudeCodeTurns
{
    public const int MaxSessions = 64;

    private readonly Dictionary<string, DateTimeOffset> _started = new(StringComparer.Ordinal);

    public AgentTurnEvent Apply(ClaudeCodeSignalFile.Signal s)
    {
        switch (s.Event)
        {
            case ClaudeCodeSignalFile.Prompt:
                if (_started.Count >= MaxSessions && !_started.ContainsKey(s.Session))
                    _started.Remove(_started.MinBy(kv => kv.Value).Key);
                _started[s.Session] = s.At;
                return new AgentTurnEvent(AgentSources.Claude, AgentTurnKind.Started, s.Session, s.Folder, s.At);
            case ClaudeCodeSignalFile.Waiting:
                return new AgentTurnEvent(AgentSources.Claude,
                    _started.ContainsKey(s.Session) ? AgentTurnKind.Waiting : AgentTurnKind.Idle, s.Session, s.Folder, s.At);
            default:
                TimeSpan? duration = _started.Remove(s.Session, out var start) && s.At >= start ? s.At - start : null;
                return new AgentTurnEvent(AgentSources.Claude, AgentTurnKind.Finished, s.Session, s.Folder, s.At, duration);
        }
    }
}
