using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// The files that bring watchers in, decoded and checked again (the Mac's <c>WatcherRequest</c>).
/// Pure: bytes in, a command or a report out, nothing kept. An agent's request,
/// <c>watch-request-*.json</c> in <c>%APPDATA%\Sanduhr</c>, is written by the MCP tools
/// <c>watch_start</c>, <c>watch_update</c> and <c>watch_end</c>:
/// <code>
/// { "schema_version": 1, "op": "start", "id": "w0123456789ab", "requested_at": "2026-10-11T12:00:00Z",
///   "title": "PR 140 CI", "short": "PR 140", "link": "https://...", "total": 12, "work": false,
///   "folder": "C:\\Users\\x\\.claude" }
/// { "schema_version": 1, "op": "update", "id": "...", "requested_at": "...", "done": 3,
///   "note": "lint passed", "state": "waiting" }
/// { "schema_version": 1, "op": "end", "id": "...", "requested_at": "...", "result": "passed" }
/// </code>
/// A Stop's report (<see cref="DecodeStop"/>) is the Stop hook's few fields, or a Stop hook's own
/// input (<c>session_id</c>, <c>background_tasks</c>): it keeps id, type, status, description and a
/// workflow's name, never <c>command</c>, never <c>last_assistant_message</c>. Nothing calls it
/// yet; the seam is here so the hook can arrive without touching the board.
/// </summary>
public static partial class WatcherRequest
{
    public const int SchemaVersion = 1;

    /// <summary>A request older than this is dropped (Sanduhr was not running when it was made).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>A request stamped further ahead than this is dropped.</summary>
    public static readonly TimeSpan MaxSkew = TimeSpan.FromSeconds(60);

    /// <summary>Largest file either kind may be.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>A decoded request. <paramref name="Folder"/> is the Claude Code folder the agent runs
    /// with, for the work tag; null for the default. Only an absolute Windows path is kept (the Mac
    /// keeps only absolute POSIX paths).</summary>
    public sealed record Decoded(WatcherCommand Command, string? Folder);

    /// <summary>An id as the server makes it ("w" and 12 hex digits); the app takes any short plain
    /// token: 1 to 64 ASCII letters, digits and "-_.:".</summary>
    public static string? ValidId(string? s)
    {
        if (s is null || s.Length is < 1 or > 64) return null;
        foreach (var c in s)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')) return null;
        }
        return s;
    }

    /// <summary>One agent request, or null for anything malformed or stale.</summary>
    public static Decoded? Decode(byte[] data, DateTimeOffset now)
    {
        if (data.Length > MaxBytes) return null;
        using var doc = Parse(data);
        if (doc is null) return null;
        var o = doc.RootElement;

        if (Int(Get(o, "schema_version")) != SchemaVersion) return null;
        if (ValidId(Str(Get(o, "id"))) is not { } id) return null;
        if (Str(Get(o, "requested_at")) is not { } stamp || ParseDate(stamp) is not { } at) return null;
        if (now - at > MaxAge || at - now > MaxSkew) return null;

        switch (Str(Get(o, "op")))
        {
            case "start":
            {
                if (WatcherLimits.Line(Str(Get(o, "title")), WatcherLimits.Title) is not { } title) return null;
                int? total = Int(Get(o, "total")) is { } t && t >= 1 && t <= WatcherLimits.Total ? t : null;
                var work = Get(o, "work") is { ValueKind: JsonValueKind.True };
                var command = new WatcherCommand.Start(id, title, WatcherLimits.Link(Str(Get(o, "link"))), total, work,
                    WatcherLimits.Line(Str(Get(o, "short")), WatcherLimits.Short));
                return new Decoded(command, Folder(Get(o, "folder")));
            }
            case "update":
            {
                WatcherState? state;
                switch (Get(o, "state"))
                {
                    case null: state = null; break;
                    case { ValueKind: JsonValueKind.String } s when s.GetString() == "running": state = WatcherState.Running; break;
                    case { ValueKind: JsonValueKind.String } s when s.GetString() == "waiting": state = WatcherState.Waiting; break;
                    default: return null;
                }
                int? done = Int(Get(o, "done")) is { } d ? Math.Min(Math.Max(0, d), WatcherLimits.Total) : null;
                return new Decoded(new WatcherCommand.Update(id, done,
                    WatcherLimits.Line(Str(Get(o, "note")), WatcherLimits.Note), state), null);
            }
            case "end":
            {
                var result = Str(Get(o, "result")) switch
                {
                    "passed" => WatcherState.Passed,
                    "failed" => WatcherState.Failed,
                    _ => (WatcherState?)null,
                };
                if (result is not { } r) return null;
                return new Decoded(new WatcherCommand.End(id, r,
                    WatcherLimits.Line(Str(Get(o, "note")), WatcherLimits.Note)), null);
            }
            default:
                return null;
        }
    }

    /// <summary>One Stop's report (the hook's file, or a Stop hook input), or null when it names no
    /// session. Only id, type, status, description (clipped) and a workflow's name are kept.</summary>
    public static StopReport? DecodeStop(byte[] data)
    {
        if (data.Length > MaxBytes) return null;
        using var doc = Parse(data);
        if (doc is null) return null;
        var o = doc.RootElement;

        if (Get(o, "schema_version") is { } v && Int(v) != SchemaVersion) return null;
        if (ValidId(Str(Get(o, "session") ?? Get(o, "session_id"))) is not { } session) return null;

        var tasks = new List<BackgroundTask>();
        if ((Get(o, "tasks") ?? Get(o, "background_tasks")) is { ValueKind: JsonValueKind.Array } raw)
        {
            foreach (var item in raw.EnumerateArray().Take(WatcherBoard.TasksPerStop))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (ValidId(Str(Get(item, "id"))) is not { } id) continue;
                tasks.Add(new BackgroundTask(
                    id,
                    WatcherLimits.Line(Str(Get(item, "type")), 20) ?? "task",
                    WatcherLimits.Line(Str(Get(item, "status")), 20) ?? "running",
                    WatcherLimits.Line(Str(Get(item, "description")), WatcherLimits.Title) ?? "",
                    WatcherLimits.Line(Str(Get(item, "name")), 60)));
            }
        }
        return new StopReport(session, Folder(Get(o, "folder")), tasks);
    }

    /// <summary>An ISO 8601 stamp with a zone ("Z" or an offset), with or without fractional
    /// seconds; null for anything else.</summary>
    public static DateTimeOffset? ParseDate(string s) =>
        StampPattern().IsMatch(s)
        && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    /// <summary>An agent request's JSON, as the server writes it (the tests and any debug action).</summary>
    public static byte[] ToJson(WatcherCommand command, DateTimeOffset at, string? folder = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("schema_version", SchemaVersion);
            w.WriteString("requested_at", at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            switch (command)
            {
                case WatcherCommand.Start s:
                    w.WriteString("op", "start");
                    w.WriteString("id", s.Id);
                    w.WriteString("title", s.Title);
                    w.WriteBoolean("work", s.Work);
                    if (s.Short is not null) w.WriteString("short", s.Short);
                    if (s.Link is not null) w.WriteString("link", s.Link.AbsoluteUri);
                    if (s.Total is { } total) w.WriteNumber("total", total);
                    if (folder is not null) w.WriteString("folder", folder);
                    break;
                case WatcherCommand.Update u:
                    w.WriteString("op", "update");
                    w.WriteString("id", u.Id);
                    if (u.Done is { } done) w.WriteNumber("done", done);
                    if (u.Note is not null) w.WriteString("note", u.Note);
                    if (u.State is { } state) w.WriteString("state", state.Wire());
                    break;
                case WatcherCommand.End e:
                    w.WriteString("op", "end");
                    w.WriteString("id", e.Id);
                    w.WriteString("result", e.Result.Wire());
                    if (e.Note is not null) w.WriteString("note", e.Note);
                    break;
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static JsonDocument? Parse(byte[] data)
    {
        try
        {
            var doc = JsonDocument.Parse(data);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A property's value, null when missing or JSON null (the Mac's nil).</summary>
    private static JsonElement? Get(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static string? Str(JsonElement? v) => v is { ValueKind: JsonValueKind.String } e ? e.GetString() : null;

    /// <summary>A whole number within a billion either way; booleans and fractions are not numbers here.</summary>
    private static int? Int(JsonElement? v)
    {
        if (v is not { ValueKind: JsonValueKind.Number } e || !e.TryGetDouble(out var d)) return null;
        if (!double.IsFinite(d) || d != Math.Round(d) || Math.Abs(d) > 1e9) return null;
        return (int)d;
    }

    /// <summary>A folder is kept only as an absolute (fully qualified) Windows path.</summary>
    private static string? Folder(JsonElement? v) =>
        Str(v) is { Length: > 0 } s && Path.IsPathFullyQualified(s) ? s : null;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex StampPattern();
}
