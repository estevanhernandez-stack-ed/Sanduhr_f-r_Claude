using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Antigravity CLI local burn (spec 3). Each <c>brain/&lt;conversation_id&gt;/.system_generated/logs/transcript.jsonl</c>
/// is one Vault session; its project comes from the summaries database (looked up by the App through
/// <c>workspaces</c>, which reads only <c>conversation_id</c> and <c>workspace_uris</c>). <c>transcript_full.jsonl</c>
/// repeats the same tokens and is never read. The IDE's folder is not read: its tokens are protobuf only.
/// </summary>
public sealed class AntigravityBurnSource(string cliHome, Func<IReadOnlyDictionary<string, string?>?> workspaces) : IAgentBurnSource
{
    public const string Model = "antigravity";   // the transcript names no model

    public string Agent => AgentSources.Antigravity;

    public static string DefaultCliHome(string userProfile) => Path.Combine(userProfile, ".gemini", "antigravity-cli");

    public static string SummariesPath(string cliHome) => Path.Combine(cliHome, "conversation_summaries.db");

    /// <summary>The first <c>file:</c> URI in a <c>workspace_uris</c> JSON array, as a local path; null otherwise.</summary>
    public static string? ProjectPathOf(string? workspaceUrisJson)
    {
        if (string.IsNullOrWhiteSpace(workspaceUrisJson)) return null;
        try
        {
            if (JsonNode.Parse(workspaceUrisJson) is not JsonArray a || a.Count == 0) return null;
            if (a[0] is not JsonValue v || !v.TryGetValue<string>(out var s)) return null;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || !u.IsFile) return null;
            // Uri.LocalPath keeps an encoded drive colon (c%3A) as "/c:/..."; decode it ourselves and drop the slash.
            var path = Uri.UnescapeDataString(u.AbsolutePath);
            if (path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':') path = path[1..];
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
        catch (JsonException) { return null; }
    }

    public IReadOnlyList<AgentBurnFile> Discover()
    {
        var brain = new DirectoryInfo(Path.Combine(cliHome, "brain"));
        if (!brain.Exists) return [];
        // Unreadable database (locked, missing, damaged): list nothing, so no checkpoint moves and the next pass reads it all.
        if (workspaces() is not { } map) return [];
        var files = new List<AgentBurnFile>();
        foreach (var conv in brain.EnumerateDirectories())
        {
            var fi = new FileInfo(Path.Combine(conv.FullName, ".system_generated", "logs", "transcript.jsonl"));
            if (!fi.Exists) continue;
            var cwd = map.TryGetValue(conv.Name, out var uris) ? ProjectPathOf(uris) : null;
            files.Add(new AgentBurnFile(fi, conv.Name, cwd, false));
        }
        return files;
    }

    public Stream Open(AgentBurnFile file) =>
        new FileStream(file.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public IBurnLineParser NewParser(string? state) => new Parser();

    /// <summary>A turn line with token fields becomes an event dated by its own <c>created_at</c>. Input is fresh input
    /// as is: the 2026-11-13 build check found <c>cache_read_tokens</c> kept apart from <c>input_tokens</c>.</summary>
    public sealed class Parser : IBurnLineParser
    {
        public BurnEvent? Parse(string line, out bool bad)
        {
            bad = false;
            if (!line.Contains("_tokens\"", StringComparison.Ordinal)) return null;   // user and tool steps carry none
            try
            {
                if (JsonNode.Parse(line) is not JsonObject d) { bad = true; return null; }
                if (BurnJson.ParseIso(BurnJson.Str(d, "created_at")) is not { } ts) return null;
                return new BurnEvent(ts, BurnJson.Num(d, "input_tokens"), BurnJson.Num(d, "output_tokens"),
                    BurnJson.Num(d, "cache_read_tokens"), 0, Model, null, null);
            }
            // Duplicate keys throw ArgumentException lazily on property access, like ClaudeLineParser.
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
            {
                bad = true;
                return null;
            }
        }

        public string? SaveState() => null;
    }
}
