using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Where the design handoff's files live (all in Sanduhr's external folder, <c>%USERPROFILE%\.sanduhr</c>).</summary>
public sealed record DeskDesignPaths(string Support)
{
    public string Request => Path.Combine(Support, DeskDesign.RequestFile);
    public string Result => Path.Combine(Support, DeskDesign.ResultFile);
    public string State => Path.Combine(Support, DeskDesign.StateFile);
}

/// <summary>What the widget tells the MCP server's caller about a proposed design.</summary>
public sealed record DeskAnswer(
    string Status,
    string? Reason = null,
    IReadOnlyList<DeskReason>? Reasons = null,
    IReadOnlyList<MissingFont>? Fonts = null,
    IReadOnlyList<string>? Changes = null,
    IReadOnlyList<DeskReason>? Warnings = null)
{
    public const string Previewing = "previewing";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string NeedsFont = "needs_font";

    /// <summary>The answer for a design that failed the rules.</summary>
    public static DeskAnswer Refused(IReadOnlyList<DeskReason> reasons) => new(Rejected, Reasons: reasons);

    /// <summary>What phase 1 of the Desk design answers a good proposal: the preview is not built in this version yet.</summary>
    public static DeskAnswer NotReady { get; } = new(Rejected, "not_ready",
        [new DeskReason("design", "not_ready: this version of Sanduhr checks designs but cannot preview them yet")]);
}

/// <summary>What <see cref="DeskDesignHandoff.Decode"/> made of a request file.</summary>
public enum DeskRequestKind { Proposal, Refused, Discard }

/// <summary>One decoded request.</summary>
public sealed record DeskRequest(DeskRequestKind Kind, string? Id, DeskValidation? Validation, IReadOnlyList<DeskReason> Reasons)
{
    public static DeskRequest Discard { get; } = new(DeskRequestKind.Discard, null, null, []);
}

/// <summary>
/// Claude's proposed Desk designs, the file half (spec, the agent-designed Desk, MCP tools). The MCP server's
/// <c>propose_desk</c> validates the design and drops <c>desk-design-request.json</c> into Sanduhr's folder; the App watches
/// the folder and calls <see cref="Check"/>, which reads the request, checks the design again (the widget is the authority;
/// it knows the machine) and hands a valid one to <see cref="Handler"/>. Every request is answered in
/// <c>desk-design-result.json</c>: <c>{id, completed_at, result: {status, ...}}</c>, written through a temp sibling and a
/// replace. A request older than ten minutes (the widget was not running) is dropped without an answer.
///
/// Phase 1's handler answers every valid design <see cref="DeskAnswer.NotReady"/>; the live preview replaces it.
/// Not thread-safe: call it from one thread.
/// </summary>
public sealed class DeskDesignHandoff
{
    public const int SchemaVersion = 1;
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(600);

    private readonly Func<DeskDesignContext> _context;
    private readonly Func<DateTimeOffset> _now;
    private byte[]? _lastState;

    public DeskDesignHandoff(DeskDesignPaths paths, Func<DeskDesignContext> context, Func<DateTimeOffset>? now = null)
    {
        Paths = paths;
        _context = context;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public DeskDesignPaths Paths { get; }

    /// <summary>What to tell the caller about a design that passed the rules (the request id, the verdict). Phase 1: <see cref="DeskAnswer.NotReady"/>.</summary>
    public Func<string, DeskValidation, DeskAnswer> Handler { get; set; } = (_, _) => DeskAnswer.NotReady;

    /// <summary>Takes a waiting request, if there is one, and answers it.</summary>
    public void Check()
    {
        if (MessageFileIo.Take(Paths.Request) is { } data) Receive(data);
    }

    /// <summary>One request's bytes, as read from the file.</summary>
    public void Receive(byte[] data)
    {
        var request = Decode(data, _now(), _context());
        switch (request.Kind)
        {
            case DeskRequestKind.Discard:
                return;
            case DeskRequestKind.Refused:
                WriteResult(request.Id!, DeskAnswer.Refused(request.Reasons));
                return;
            default:
                WriteResult(request.Id!, Handler(request.Id!, request.Validation!));
                return;
        }
    }

    /// <summary>Reads a request: <c>{schema_version, id, requested_at, design}</c>. Unreadable, without an id, or too old is
    /// dropped; a design that breaks a rule is refused with the reasons; a valid one (or one that needs a font) is a proposal.</summary>
    public static DeskRequest Decode(byte[] data, DateTimeOffset now, DeskDesignContext context)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(data); }
        catch (JsonException) { return DeskRequest.Discard; }
        if (root is not JsonObject o) return DeskRequest.Discard;
        if (!(o["id"] is JsonValue iv && iv.TryGetValue<string>(out var id) && id.Length is > 0 and <= 64)) return DeskRequest.Discard;
        if (!(o["requested_at"] is JsonValue tv && tv.TryGetValue<string>(out var stamp) && MessageProposal.ParseDate(stamp) is { } at))
            return new DeskRequest(DeskRequestKind.Refused, id, null, [new DeskReason("requested_at", "is missing or not a date")]);
        if (now - at > MaxAge) return DeskRequest.Discard;
        if (!(o["schema_version"] is JsonValue sv && sv.TryGetValue<int>(out var version) && version == SchemaVersion))
            return new DeskRequest(DeskRequestKind.Refused, id, null, [new DeskReason("schema_version", "this Sanduhr reads requests of schema 1")]);
        var verdict = DeskDesign.Validate(o["design"], context);
        return verdict.Status == DeskDesignStatus.Rejected
            ? new DeskRequest(DeskRequestKind.Refused, id, verdict, verdict.Reasons)
            : new DeskRequest(DeskRequestKind.Proposal, id, verdict, []);
    }

    /// <summary>The result file's contents for an answer.</summary>
    public static byte[] ResultJson(string id, DeskAnswer answer, DateTimeOffset now)
    {
        var result = new JsonObject { ["status"] = answer.Status };
        if (answer.Reason is not null) result["reason"] = answer.Reason;
        if (answer.Reasons is { Count: > 0 } reasons)
        {
            var arr = new JsonArray();
            foreach (var r in reasons) arr.Add((JsonNode?)r.ToJson());
            result["reasons"] = arr;
        }
        if (answer.Fonts is { Count: > 0 } fonts)
        {
            var arr = new JsonArray();
            foreach (var f in fonts) arr.Add((JsonNode?)f.ToJson());
            result["fonts"] = arr;
        }
        if (answer.Changes is { Count: > 0 } changes)
        {
            var arr = new JsonArray();
            foreach (var c in changes) arr.Add((JsonNode?)c);
            result["changes"] = arr;
        }
        if (answer.Warnings is { Count: > 0 } warnings)
        {
            var arr = new JsonArray();
            foreach (var w in warnings) arr.Add((JsonNode?)w.ToJson());
            result["warnings"] = arr;
        }
        var root = new JsonObject
        {
            ["completed_at"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["id"] = id,
            ["result"] = result,
        };
        return new UTF8Encoding(false).GetBytes(root.ToJsonString());
    }

    private void WriteResult(string id, DeskAnswer answer) => TryWrite(Paths.Result, ResultJson(id, answer, _now()));

    /// <summary>
    /// Writes <c>desk-state.json</c> (only when its bytes changed since the last write here). Never throws; false when the
    /// write failed.
    /// </summary>
    public bool WriteState(JsonObject state)
    {
        var data = new UTF8Encoding(false).GetBytes(state.ToJsonString());
        if (_lastState is not null && data.AsSpan().SequenceEqual(_lastState)) return true;
        if (!TryWrite(Paths.State, data)) return false;
        _lastState = data;
        return true;
    }

    /// <summary>Removes <c>desk-state.json</c> (the MCP integration is off, or Reset ran). Never throws.</summary>
    public void DeleteState()
    {
        _lastState = null;
        try { if (File.Exists(Paths.State)) File.Delete(Paths.State); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next change tries again */ }
    }

    private static bool TryWrite(string path, byte[] data)
    {
        try
        {
            MessageFileIo.WriteAtomic(path, data);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
