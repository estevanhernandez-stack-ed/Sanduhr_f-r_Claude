using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// Resolved server configuration. Two env overrides exist for the cold-verify
/// fixtures ONLY (read-redirection, dev-tier — never set by shipped config):
/// <c>SANDUHR_SNAPSHOT_PATH</c> redirects the snapshot read;
/// <c>SANDUHR_CC_ROOTS</c> (';'-separated directory list) replaces root
/// discovery + consent. Without them the server reads the real
/// <c>%APPDATA%\Sanduhr</c> paths and honors the <c>mcp_roots</c> consent map
/// in settings.json — a root absent from the map (or the map absent entirely)
/// is NOT consented; the burn surfaces return <c>no_data/disabled</c>.
/// </summary>
public sealed class McpConfig
{
    /// <summary>Full path of snapshot.json.</summary>
    public required string SnapshotPath { get; init; }

    /// <summary>Consented CC roots: name → full path. Order is stable
    /// (.claude before .claude-personal). Empty = nothing consented.</summary>
    public required IReadOnlyList<(string Name, string Path)> ConsentedRoots { get; init; }

    /// <summary>All CC roots that exist on this machine (consented or not) —
    /// ping reports both so a cold agent can tell "no roots" from "no consent".</summary>
    public required IReadOnlyList<string> RootsFound { get; init; }

    private static readonly string[] KnownRootNames = { ".claude", ".claude-personal" };

    public static McpConfig Resolve()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string sanduhrDir = Path.Combine(appData, "Sanduhr");

        string snapshotPath = Environment.GetEnvironmentVariable("SANDUHR_SNAPSHOT_PATH")
            is { Length: > 0 } envSnap
            ? envSnap
            : Path.Combine(sanduhrDir, "snapshot.json");

        var found = new List<string>();
        foreach (var name in KnownRootNames)
        {
            if (Directory.Exists(Path.Combine(home, name)))
                found.Add(name);
        }

        var consented = new List<(string, string)>();
        if (Environment.GetEnvironmentVariable("SANDUHR_CC_ROOTS") is { Length: > 0 } envRoots)
        {
            // Dev-tier fixture redirection: each entry is a directory that plays
            // the role of a CC root; name = its basename.
            foreach (var p in envRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Directory.Exists(p))
                    consented.Add((Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), p));
            }
        }
        else
        {
            // Production path: the closed root-name set gated by the consent map.
            // No free-form paths, ever (design review: the file-oracle guard).
            var consentMap = ReadMcpRootsConsent(Path.Combine(sanduhrDir, "settings.json"));
            foreach (var name in KnownRootNames)
            {
                string rootPath = Path.Combine(home, name);
                if (consentMap.TryGetValue(name, out bool on) && on && Directory.Exists(rootPath))
                    consented.Add((name, rootPath));
            }
        }

        return new McpConfig
        {
            SnapshotPath = snapshotPath,
            ConsentedRoots = consented,
            RootsFound = found,
        };
    }

    private static Dictionary<string, bool> ReadMcpRootsConsent(string settingsPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(settingsPath))
                return result;
            if (JsonNode.Parse(File.ReadAllText(settingsPath)) is not JsonObject root)
                return result;
            if (root["mcp_roots"] is not JsonObject map)
                return result;
            foreach (var (name, node) in map)
            {
                try { result[name] = node?.GetValue<bool>() ?? false; }
                catch { result[name] = false; }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable settings = nothing consented. Fail closed.
        }
        return result;
    }
}
