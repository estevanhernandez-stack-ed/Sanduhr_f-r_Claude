using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>
/// Codex local burn (spec 3). Walks <c>sessions/**/rollout-*.jsonl</c> and <c>.jsonl.zst</c> under every Codex home. One
/// rollout is one Vault session, keyed by its name without extensions, so a rollout Codex compresses later writes the same
/// row. Reads only timestamps, segment ids, cwd, model names and token counts; never message text or account ids.
/// </summary>
public sealed class CodexBurnSource(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst) : IAgentBurnSource
{
    private const string Plain = ".jsonl";
    private const string Zst = ".jsonl.zst";

    public string Agent => AgentSources.Codex;

    public static string SessionIdOf(string fileName) =>
        fileName.EndsWith(Zst, StringComparison.OrdinalIgnoreCase) ? fileName[..^Zst.Length]
        : fileName.EndsWith(Plain, StringComparison.OrdinalIgnoreCase) ? fileName[..^Plain.Length]
        : fileName;

    public IReadOnlyList<AgentBurnFile> Discover()
    {
        var files = new List<AgentBurnFile>();
        foreach (var home in homes())
        {
            var sessions = new DirectoryInfo(Path.Combine(home, "sessions"));
            if (!sessions.Exists) continue;
            foreach (var fi in sessions.EnumerateFiles("rollout-*", SearchOption.AllDirectories))
            {
                bool zst = fi.Name.EndsWith(Zst, StringComparison.OrdinalIgnoreCase);
                bool plain = fi.Name.EndsWith(Plain, StringComparison.OrdinalIgnoreCase);
                if (!plain && !zst) continue;
                if (zst && decompressZst is null) continue;   // left unread until a decompressor is available
                files.Add(new AgentBurnFile(fi, SessionIdOf(fi.Name), null, zst));
            }
        }
        return files;
    }

    public Stream Open(AgentBurnFile file)
    {
        var fs = new FileStream(file.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!file.Compressed) return fs;
        try { return new CappedReadStream(decompressZst!(fs), CodexLimitsReader.MaxDecompressedBytes); }
        catch { fs.Dispose(); throw; }
    }

    public IBurnLineParser NewParser(string? state) => new CodexLineParser(state);
}

/// <summary>
/// Codex rollout lines. <c>session_meta</c> opens a segment and resets the running total; <c>turn_context</c> sets the
/// model and cwd in force; a <c>token_count</c> event counts the change in <c>info.total_token_usage</c> since the
/// segment's previous reading (a repeated reading counts zero; a total that drops counts <c>last_token_usage</c>).
/// Fresh input is <c>input_tokens - cached_input_tokens</c>; output already includes reasoning.
/// </summary>
public sealed class CodexLineParser : IBurnLineParser
{
    private State _s;
    private string? _cwd;   // this pass only: never persisted, a resumed parser learns it from the next session_meta/turn_context

    public CodexLineParser(string? state)
    {
        _s = new State();
        if (state is null) return;
        try { _s = JsonSerializer.Deserialize<State>(state) ?? new State(); }
        catch (JsonException) { _s = new State(); }   // an unreadable state restarts the segment total, never crashes
        _s.Last ??= new Usage();
    }

    public BurnEvent? Parse(string line, out bool bad)
    {
        try { return ParseCore(line, out bad); }
        // Duplicate keys throw ArgumentException lazily on access; a malformed line is a bad line, never a thrown one.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { bad = true; return null; }
    }

    public BurnEvent? ParseCore(string line, out bool bad)
    {
        bad = false;
        if (!line.Contains("\"session_meta\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_context\"", StringComparison.Ordinal)
            && !line.Contains("\"token_count\"", StringComparison.Ordinal))
            return null;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { bad = true; return null; }
        if (node is not JsonObject d || d["payload"] is not JsonObject p) { bad = true; return null; }

        switch (BurnJson.Str(d, "type"))
        {
            case "session_meta":
                _cwd = BurnJson.Str(p, "cwd") ?? _cwd;
                _s.HasTotal = false;
                _s.Last = new Usage();
                return null;
            case "turn_context":
                _s.Model = BurnJson.Str(p, "model") ?? _s.Model;
                _cwd = BurnJson.Str(p, "cwd") ?? _cwd;
                return null;
            case "event_msg" when BurnJson.Str(p, "type") == "token_count":
                return Count(d, p);
            default:
                return null;
        }
    }

    private BurnEvent? Count(JsonObject d, JsonObject p)
    {
        if (p["info"] is not JsonObject info) return null;
        if (BurnJson.ParseIso(BurnJson.Str(d, "timestamp")) is not { } ts) return null;
        var total = info["total_token_usage"] as JsonObject;
        var last = info["last_token_usage"] as JsonObject;

        Usage u;
        if (total is not null)
        {
            var t = Usage.Of(total);
            u = !_s.HasTotal ? t
                : t.Total >= _s.Last.Total ? t.Minus(_s.Last)
                : last is not null ? Usage.Of(last) : t;
            _s.Last = t;
            _s.HasTotal = true;
        }
        else if (last is not null) u = Usage.Of(last);
        else return null;

        if (u.Input == 0 && u.Output == 0 && u.Cached == 0 && u.CacheWrite == 0) return null;
        return new BurnEvent(ts, Math.Max(0, u.Input - u.Cached), u.Output, u.Cached, u.CacheWrite, _s.Model, _cwd, null);
    }

    public string? SaveState() => JsonSerializer.Serialize(_s);

    private sealed class State
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("has_total")] public bool HasTotal { get; set; }
        [JsonPropertyName("last")] public Usage Last { get; set; } = new();
    }

    private sealed class Usage
    {
        [JsonPropertyName("in")] public long Input { get; set; }
        [JsonPropertyName("cached")] public long Cached { get; set; }
        [JsonPropertyName("cache_write")] public long CacheWrite { get; set; }
        [JsonPropertyName("out")] public long Output { get; set; }
        [JsonPropertyName("total")] public long Total { get; set; }

        public static Usage Of(JsonObject o) => new()
        {
            Input = BurnJson.Num(o, "input_tokens"),
            Cached = BurnJson.Num(o, "cached_input_tokens"),
            CacheWrite = BurnJson.Num(o, "cache_write_input_tokens"),
            Output = BurnJson.Num(o, "output_tokens"),
            Total = BurnJson.Num(o, "total_tokens"),
        };

        public Usage Minus(Usage b) => new()
        {
            Input = Math.Max(0, Input - b.Input),
            Cached = Math.Max(0, Cached - b.Cached),
            CacheWrite = Math.Max(0, CacheWrite - b.CacheWrite),
            Output = Math.Max(0, Output - b.Output),
            Total = Math.Max(0, Total - b.Total),
        };
    }
}
