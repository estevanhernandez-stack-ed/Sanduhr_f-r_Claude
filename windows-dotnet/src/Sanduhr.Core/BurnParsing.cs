using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One token reading from any agent's log (spec 3). Cache counts are kept apart from Input: Input is fresh
/// input only, so Input + Output is the total every Vault row sums.</summary>
public readonly record struct BurnEvent(
    DateTimeOffset Timestamp, long Input, long Output, long CacheRead, long CacheCreation,
    string? Model, string? Cwd, string? Skill);

/// <summary>Turns one log line into a <see cref="BurnEvent"/>. One instance reads one file, in order.</summary>
public interface IBurnLineParser
{
    /// <summary>Parse one trimmed, non-empty line. Null when the line carries no event. <paramref name="bad"/> is true for a
    /// line that should count in the row's skipped_lines (it looked like an event and did not parse).</summary>
    BurnEvent? Parse(string line, out bool bad);

    /// <summary>State to resume mid-file from the checkpoint, or null for a stateless parser.</summary>
    string? SaveState();
}

/// <summary>Claude Code transcript lines, exactly as VaultIngester read them before spec 3.</summary>
public sealed class ClaudeLineParser : IBurnLineParser
{
    public BurnEvent? Parse(string line, out bool bad)
    {
        try { return ParseCore(line, out bad); }
        // Duplicate keys throw ArgumentException lazily on access; a malformed line is a bad line, never a thrown one.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { bad = true; return null; }
    }

    public BurnEvent? ParseCore(string line, out bool bad)
    {
        bad = false;
        // Perf prefilter only: looser than "type":"assistant" on purpose so formatting drift can never silently drop events.
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal))
            return null;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { bad = true; return null; }
        if (node is not JsonObject d) { bad = true; return null; }
        if (BurnJson.Str(d, "type") != "assistant")
            return null;
        if (d["message"] is not JsonObject msg || msg["usage"] is not JsonObject usage)
            return null;
        if (BurnJson.ParseIso(BurnJson.Str(d, "timestamp")) is not { } ts)
            return null;   // matches the live reader: untimestamped events don't count
        return new BurnEvent(ts,
            BurnJson.Num(usage, "input_tokens"), BurnJson.Num(usage, "output_tokens"),
            BurnJson.Num(usage, "cache_read_input_tokens"), BurnJson.Num(usage, "cache_creation_input_tokens"),
            BurnJson.Str(msg, "model"), BurnJson.Str(d, "cwd"), BurnJson.Str(d, "attributionSkill"));
    }

    public string? SaveState() => null;
}

/// <summary>JSON helpers the burn parsers share.</summary>
internal static class BurnJson
{
    public static string? Str(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static long Num(JsonObject? o, string key)
    {
        if (o?[key] is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return 0;
    }

    public static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return DateTimeOffset.TryParse(s.Replace("Z", "+00:00"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }
}
