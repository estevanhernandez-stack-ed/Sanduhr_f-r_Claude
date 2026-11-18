using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Codex's turn markers (5.0 spec 4). One reader per rollout, fed every line in order. <c>task_started</c> opens a turn,
/// <c>task_complete</c> finishes it and <c>turn_aborted</c> stops it; the project is the <c>cwd</c> of the
/// <c>turn_context</c> in force. A rollout whose <c>session_meta</c> names a parent thread is a sub-agent's: it raises
/// nothing, and that mark sticks, because a forked rollout repeats its parent's meta without the keys.
/// Reads timestamps, ids, folders and the duration; never <c>last_agent_message</c> or any other text.
/// </summary>
public sealed class CodexTurnReader(string session)
{
    private string? _cwd;
    private DateTimeOffset? _started;

    public bool IsChild { get; private set; }

    public bool TurnOpen => _started is not null;

    public AgentTurnEvent? Read(string line)
    {
        try { return ReadCore(line); }
        // Duplicate keys throw ArgumentException lazily on access; a malformed line is skipped, never thrown.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private AgentTurnEvent? ReadCore(string line)
    {
        if (!line.Contains("\"session_meta\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_context\"", StringComparison.Ordinal)
            && !line.Contains("\"task_started\"", StringComparison.Ordinal)
            && !line.Contains("\"task_complete\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_aborted\"", StringComparison.Ordinal))
            return null;
        if (JsonNode.Parse(line) is not JsonObject d || d["payload"] is not JsonObject p) return null;

        switch (BurnJson.Str(d, "type"))
        {
            case "session_meta":
                _cwd = BurnJson.Str(p, "cwd") ?? _cwd;
                IsChild |= HasText(p, "parent_thread_id") || HasText(p, "forked_from_id");
                return null;
            case "turn_context":
                _cwd = BurnJson.Str(p, "cwd") ?? _cwd;
                return null;
            case "event_msg":
                break;
            default:
                return null;
        }

        if (BurnJson.ParseIso(BurnJson.Str(d, "timestamp")) is not { } at) return null;
        switch (BurnJson.Str(p, "type"))
        {
            case "task_started":
                _started = at;
                return Raise(AgentTurnKind.Started, at, null);
            case "task_complete":
            {
                var duration = LoggedDuration(p) ?? Since(at);
                _started = null;
                return Raise(AgentTurnKind.Finished, at, duration);
            }
            case "turn_aborted":
            {
                var duration = Since(at);
                _started = null;
                return Raise(AgentTurnKind.Stopped, at, duration);
            }
            default:
                return null;
        }
    }

    private AgentTurnEvent? Raise(AgentTurnKind kind, DateTimeOffset at, TimeSpan? duration) =>
        IsChild ? null : new AgentTurnEvent(AgentSources.Codex, kind, session, _cwd, at, duration);

    private TimeSpan? Since(DateTimeOffset at) => _started is { } s && at >= s ? at - s : null;

    private static TimeSpan? LoggedDuration(JsonObject p) =>
        p["duration_ms"] is JsonValue v && v.TryGetValue<double>(out var ms) && ms >= 0 ? TimeSpan.FromMilliseconds(ms) : null;

    private static bool HasText(JsonObject p, string key) =>
        p[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0;
}
