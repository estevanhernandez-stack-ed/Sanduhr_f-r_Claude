using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

/// <summary>
/// Resolved server configuration. Two env overrides exist for the cold-verify
/// fixtures ONLY (read-redirection, dev-tier — never set by shipped config):
/// <c>SANDUHR_SNAPSHOT_PATH</c> redirects the snapshot read;
/// <c>SANDUHR_CC_ROOTS</c> (';'-separated directory list) replaces root
/// discovery + consent. Without them the server reads the plan B external
/// folder <c>%USERPROFILE%\.sanduhr</c> (snapshot, handoffs, the
/// <c>mcp-settings.json</c> projection) and honors that projection's
/// <c>mcp_roots</c> consent map. Roots are the folders <c>ClaudeCodeHomes.Discover</c> finds
/// (<c>~/.claude</c>, <c>~/.claude-*</c>, the <c>CLAUDE_CONFIG_DIR</c> folder when it
/// sits in the profile); a root absent from the map (or the map absent entirely)
/// is NOT consented; the burn surfaces return <c>no_data/disabled</c>.
/// </summary>
public sealed class McpConfig
{
    /// <summary>Full path of snapshot.json.</summary>
    public required string SnapshotPath { get; init; }

    /// <summary>The real usage-vault root (%LOCALAPPDATA%\Sanduhr\vault, or the
    /// SANDUHR_VAULT_DIR dev override). Read-only from this process. Reads go
    /// through <see cref="VaultDirs"/>.</summary>
    public required string VaultDir { get; init; }

    private IReadOnlyList<string>? _vaultDirs;

    /// <summary>Vault directories in search order. The Store build's NEW vault files are
    /// virtualized into the package's LocalCache; per file the LocalCache copy shadows the
    /// real one for the widget (measured 2026-10-20), so the server applies the same rule:
    /// per root and month, the first directory holding the rollup shard wins. Defaults to
    /// just <see cref="VaultDir"/>.</summary>
    public IReadOnlyList<string> VaultDirs
    {
        get => _vaultDirs ?? new[] { VaultDir };
        init => _vaultDirs = value;
    }

    /// <summary>Consented CC roots: name → full path. Order is stable
    /// (.claude first, then .claude-* in natural order). Empty = nothing consented.</summary>
    public required IReadOnlyList<(string Name, string Path)> ConsentedRoots { get; init; }

    /// <summary>All CC roots that exist on this machine (consented or not) —
    /// ping reports both so a cold agent can tell "no roots" from "no consent".</summary>
    public required IReadOnlyList<string> RootsFound { get; init; }

    /// <summary>Directory holding <c>history.&lt;account&gt;.json</c> — the widget's
    /// per-fetch append log, read as the fallback source when the snapshot is
    /// missing or dead (issue #63). History stays private in AppData
    /// (<c>%APPDATA%\Sanduhr</c>; it serves pre-snapshot widgets that write there),
    /// except when SANDUHR_SNAPSHOT_PATH redirects the snapshot: then it follows
    /// that file's directory. Null = the snapshot's own directory.</summary>
    public string? HistoryDir { get; init; }

    /// <summary><c>mcp-settings.json</c> in the external folder — the projection the
    /// widget writes of settings.json (schema_version 1, mcp_roots, publish readiness).
    /// Read (never written) for consent and by publish_usage. Null = unavailable.</summary>
    public string? McpSettingsPath { get; init; }

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

    /// <summary>The widget's external folder (<c>%USERPROFILE%\.sanduhr</c>): where the watcher
    /// requests are queued and <c>watchers.json</c> (the user's switch) is read. Null = unavailable.</summary>
    public string? ExternalDir { get; init; }

    /// <summary>The Desk messages handoff (see Core's MessageProposal): propose_desk_messages writes the
    /// request, the widget checks it again and answers in the result; get_desk_messages reads the state
    /// file the widget keeps of the pin and rotation. Null = handoff unavailable.</summary>
    public string? DeskRequestPath { get; init; }
    public string? DeskResultPath { get; init; }
    public string? DeskStatePath { get; init; }

    /// <summary>Where the user's <c>messages.txt</c> may be, in search order: read, never written, by
    /// get_desk_messages. On the Store build the widget's file lives in the package's LocalCache (MSIX
    /// AppData virtualization, the vault's rule: per file the cache copy shadows the real one), which a
    /// process outside the package does not see through <c>%APPDATA%</c>; so the cache copy is looked
    /// at first, then <c>%APPDATA%\Sanduhr\messages.txt</c>. Null = unavailable.</summary>
    public IReadOnlyList<string>? DeskMessagesPaths { get; init; }

    /// <summary>How long propose_desk_messages waits for the widget's first answer (it answers
    /// pending_approval at once when it is running).</summary>
    public TimeSpan DeskWaitTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan DeskPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public static McpConfig Resolve()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Resolve(
            home,
            Path.Combine(home, ".sanduhr"),
            Path.Combine(appData, "Sanduhr"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable);
    }

    /// <summary>Testable core of <see cref="Resolve()"/>: every machine-specific input is a
    /// parameter. <paramref name="env"/> reads <c>SANDUHR_SNAPSHOT_PATH</c>,
    /// <c>SANDUHR_VAULT_DIR</c>, <c>SANDUHR_CC_ROOTS</c> and <c>CLAUDE_CONFIG_DIR</c>.
    /// Public because the server assembly declares no InternalsVisibleTo.</summary>
    public static McpConfig Resolve(string home, string externalDir, string appDataSanduhrDir,
        string localAppData, Func<string, string?> env)
    {
        bool snapOverride = env("SANDUHR_SNAPSHOT_PATH") is { Length: > 0 };
        string snapshotPath = env("SANDUHR_SNAPSHOT_PATH")
            is { Length: > 0 } envSnap
            ? envSnap
            : Path.Combine(externalDir, "snapshot.json");

        string realVault = Path.Combine(localAppData, "Sanduhr", "vault");
        IReadOnlyList<string> vaultDirs;
        string vaultDir;
        if (env("SANDUHR_VAULT_DIR") is { Length: > 0 } envVault)
        {
            vaultDir = envVault;
            vaultDirs = new[] { envVault };
        }
        else
        {
            // Fixed paths only, no path from a file. The Store build's new vault files
            // are virtualized into the package's LocalCache; per file the LocalCache copy
            // shadows the real one for the widget (measured 2026-10-20), so the server
            // applies the same rule: cache first, then real.
            string cacheVault = Path.Combine(localAppData, "Packages",
                "626LabsLLC.SanduhrfrClaude_wz1chhb2h2v4a", "LocalCache", "Local", "Sanduhr", "vault");
            vaultDir = realVault;
            vaultDirs = new[] { cacheVault, realVault };
        }

        // Mirrors Core's external-folder file name for the consent/publish projection.
        string mcpSettingsPath = Path.Combine(externalDir, "mcp-settings.json");

        // Discovery gives names only; a path is built solely from a discovered name.
        var found = ClaudeCodeHomes.Discover(home, env("CLAUDE_CONFIG_DIR"));

        var consented = new List<(string, string)>();
        if (env("SANDUHR_CC_ROOTS") is { Length: > 0 } envRoots)
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
            // Production path: discovered names under a fixed pattern (~/.claude and
            // ~/.claude-*, the CLAUDE_CONFIG_DIR folder only when it is in the profile),
            // consent required per name. Still never a free-form path: the consent map is
            // only looked up BY a discovered name, so a key that is not one is ignored, and
            // folders added by hand in the widget never reach this process (design review:
            // the file-oracle guard; 2026-10-11 ruling).
            var consentMap = ReadMcpRootsConsent(mcpSettingsPath);
            foreach (var name in found)
            {
                if (consentMap.TryGetValue(name, out bool on) && on)
                    consented.Add((name, Path.Combine(home, name)));
            }
        }

        return new McpConfig
        {
            SnapshotPath = snapshotPath,
            VaultDir = vaultDir,
            VaultDirs = vaultDirs,
            // History stays private in AppData (pre-snapshot widgets write it there);
            // the dev-tier snapshot redirect moves it with the snapshot.
            HistoryDir = snapOverride ? Path.GetDirectoryName(snapshotPath) : appDataSanduhrDir,
            ConsentedRoots = consented,
            RootsFound = found,
            McpSettingsPath = mcpSettingsPath,
            // Mirrors Core's PublishHandoff.RequestFileName/ResultFileName literally
            // (this project cannot link that file without widening its allowlist).
            PublishRequestPath = Path.Combine(externalDir, "publish-request.json"),
            PublishResultPath = Path.Combine(externalDir, "publish-result.json"),
            // Same mirror discipline for Core's ThemeHandoff file names.
            ThemeRequestPath = Path.Combine(externalDir, "theme-request.json"),
            ThemeResultPath = Path.Combine(externalDir, "theme-result.json"),
            // The watchers and Desk messages handoffs: the file names come from the linked Core
            // types (WatcherRequest, MessageProposal); the folders are fixed, never from a file.
            ExternalDir = externalDir,
            DeskRequestPath = Path.Combine(externalDir, MessageProposal.RequestFile),
            DeskResultPath = Path.Combine(externalDir, MessageProposal.ResultFile),
            DeskStatePath = Path.Combine(externalDir, MessageProposal.StateFile),
            DeskMessagesPaths = env("SANDUHR_VAULT_DIR") is { Length: > 0 }
                ? new[] { Path.Combine(appDataSanduhrDir, "messages.txt") }
                : new[]
                {
                    Path.Combine(localAppData, "Packages", "626LabsLLC.SanduhrfrClaude_wz1chhb2h2v4a",
                        "LocalCache", "Roaming", "Sanduhr", "messages.txt"),
                    Path.Combine(appDataSanduhrDir, "messages.txt"),
                },
        };
    }

    /// <summary>True only for the JSON number 1. Shared by every projection reader.</summary>
    internal static bool IsSchemaV1(JsonNode? node)
    {
        try { return node is JsonValue v && v.TryGetValue<int>(out int n) && n == 1; }
        catch { return false; }
    }

    private static Dictionary<string, bool> ReadMcpRootsConsent(string projectionPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(projectionPath))
                return result;
            if (JsonNode.Parse(File.ReadAllText(projectionPath)) is not JsonObject root)
                return result;
            if (!IsSchemaV1(root["schema_version"]))
                return result;   // another schema = nothing consented (fail closed)
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
