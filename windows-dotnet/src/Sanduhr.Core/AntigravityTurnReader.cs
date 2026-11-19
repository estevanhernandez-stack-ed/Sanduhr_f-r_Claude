using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The Antigravity CLI's turns (5.0 spec 4). Its transcript has no turn-end marker, so the end is inferred: a
/// <c>PLANNER_RESPONSE</c> with status <c>DONE</c> and no <c>tool_calls</c> key, followed by <see cref="Quiet"/> with no
/// new line. On the 2026-11-17 sample a step that looked final was followed by more work up to 83 seconds later, so
/// 90 seconds is the shortest wait that raised nothing falsely. Only whether <c>tool_calls</c> exists is read, never
/// its value, and never a step's <c>content</c> or <c>thinking</c>.
/// </summary>
public sealed class AntigravityTurnReader(string session)
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(90);

    private DateTimeOffset? _turnStart;   // the turn's USER_INPUT created_at
    private bool _open;
    private DateTimeOffset? _finalAt;     // created_at of a step that looks final
    private DateTimeOffset? _finalSeen;   // when that line was read

    public bool TurnOpen => _open;

    /// <summary>When <see cref="Due"/> could next raise; null with nothing pending.</summary>
    public DateTimeOffset? DueAt => _finalSeen is { } seen ? seen + Quiet : null;

    /// <summary>A line arrived that was not read (an oversized one): more work, so a pending end is off.</summary>
    public void Activity()
    {
        _finalAt = null;
        _finalSeen = null;
    }

    public AgentTurnEvent? Read(string line, string? folder, DateTimeOffset now)
    {
        Activity();
        try { return ReadCore(line, folder, now); }
        // Duplicate keys throw ArgumentException lazily on access; a malformed line is skipped, never thrown.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private AgentTurnEvent? ReadCore(string line, string? folder, DateTimeOffset now)
    {
        if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject d) return null;
        var type = BurnJson.Str(d, "type");
        if (BurnJson.ParseIso(BurnJson.Str(d, "created_at")) is not { } at) return null;

        if (type == "USER_INPUT")
        {
            _turnStart = at;
            _open = true;
            return new AgentTurnEvent(AgentSources.Antigravity, AgentTurnKind.Started, session, folder, at);
        }
        if (type == "PLANNER_RESPONSE" && BurnJson.Str(d, "status") == "DONE" && !d.ContainsKey("tool_calls"))
        {
            _finalAt = at;
            _finalSeen = now;
        }
        return null;
    }

    /// <summary>Finished, once a step that looks final has had <see cref="Quiet"/> with no new line.</summary>
    public AgentTurnEvent? Due(DateTimeOffset now, string? folder)
    {
        if (_finalSeen is not { } seen || _finalAt is not { } at || now - seen < Quiet) return null;
        TimeSpan? duration = _turnStart is { } s && at >= s ? at - s : null;
        Close();
        return new AgentTurnEvent(AgentSources.Antigravity, AgentTurnKind.Finished, session, folder, at, duration);
    }

    /// <summary>After reading history at launch: a pending end closes its turn with no event, since nothing replays.</summary>
    public void Settle()
    {
        if (_finalSeen is not null) Close();
    }

    private void Close()
    {
        _turnStart = null;
        _open = false;
        Activity();
    }
}
