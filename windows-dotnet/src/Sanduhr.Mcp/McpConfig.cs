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

    /// <summary>The usage-vault root (%LOCALAPPDATA%\Sanduhr\vault, or the
    /// SANDUHR_VAULT_DIR dev override). Read-only from this process.</summary>
    public required string VaultDir { get; init; }

    /// <summary>Consented CC roots: name → full path. Order is stable
    /// (.claude before .claude-personal). Empty = nothing consented.</summary>
    public required IReadOnlyList<(string Name, string Path)> ConsentedRoots { get; init; }

    /// <summary>All CC roots that exist on this machine (consented or not) —
    /// ping reports both so a cold agent can tell "no roots" from "no consent".</summary>
    public required IReadOnlyList<string> RootsFound { get; init; }

    /// <summary>Directory holding <c>history.&lt;account&gt;.json</c> — the widget's
    /// per-fetch append log, read as the fallback source when the snapshot is
    /// missing or dead (issue #63). Null = the snapshot's own directory.</summary>
    public string? HistoryDir { get; init; }

    /// <summary>settings.json — read (never written) by publish_usage for the
    /// publish group's toggle, endpoint URL, auth scheme and token-present
    /// mirror. Null = unavailable.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>The publish handoff files (see Core's PublishHandoff): the tool
    /// writes the request, the widget writes the result. Null = handoff unavailable.</summary>
    public string? PublishRequestPath { get; init; }
    public string? PublishResultPath { get; init; }

    /// <summary>How long publish_usage waits for the widget to answer before
    /// returning a typed "queued" result. The widget's tick runs every 30s.</summary>
    public TimeSpan PublishWaitTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan PublishPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The theme handoff files (see Core's ThemeHandoff): propose_theme
    /// writes the request, the widget lints, saves, applies, and writes the
    /// result. Null = handoff unavailable.</summary>
    public string? ThemeRequestPath { get; init; }
    public string? ThemeResultPath { get; init; }

    /// <summary>How long propose_theme waits for the widget. A theme apply takes
    /// milliseconds and the widget watches the folder, so this covers a busy or
    /// starting widget, not a tick.</summary>
    public TimeSpan ThemeWaitTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ThemePollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

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

        string vaultDir = Environment.GetEnvironmentVariable("SANDUHR_VAULT_DIR")
            is { Length: > 0 } envVault
            ? envVault
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sanduhr", "vault");

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
            VaultDir = vaultDir,
            // Follows the snapshot: the dev-tier redirect moves both together.
            HistoryDir = Path.GetDirectoryName(snapshotPath),
            ConsentedRoots = consented,
            RootsFound = found,
            SettingsPath = Path.Combine(sanduhrDir, "settings.json"),
            // Mirrors Core's PublishHandoff.RequestFileName/ResultFileName literally
            // (this project cannot link that file without widening its allowlist).
            PublishRequestPath = Path.Combine(sanduhrDir, "publish-request.json"),
            PublishResultPath = Path.Combine(sanduhrDir, "publish-result.json"),
            // Same mirror discipline for Core's ThemeHandoff file names.
            ThemeRequestPath = Path.Combine(sanduhrDir, "theme-request.json"),
            ThemeResultPath = Path.Combine(sanduhrDir, "theme-result.json"),
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
