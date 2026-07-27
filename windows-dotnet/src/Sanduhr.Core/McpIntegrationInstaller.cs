using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Outcome of a Register call — <c>PriorCommand</c> carries what an
/// existing <c>sanduhr</c> entry pointed at before re-owning (dual-install
/// collision: last explicit install wins, but the UI names what it replaced).</summary>
public readonly record struct McpRegisterResult(bool Ok, string? PriorCommand);

/// <summary>
/// Installs / removes the Sanduhr MCP server integration for Claude Code
/// (WS-E registration slice). Three moving parts, all under user-owned paths:
///
/// 1. <b>Versioned server dirs</b> — <c>%APPDATA%\Sanduhr\mcp\&lt;stamp&gt;\</c>.
///    Every install copies the server build into a NEW stamped folder. This is
///    the update-under-lock answer: parallel Claude Code sessions pin the exe
///    they launched, so updating in place would fail — a new folder never
///    fights the lock, and stale folders get pruned once unpinned.
/// 2. <b>One launcher</b> — <c>%APPDATA%\Sanduhr\bin\sanduhr-mcp.cmd</c>, the
///    single registration string across channels. Rewritten to point at the
///    current version dir on every install / health-check (the widget is the
///    integration's updater, statusline precedent).
/// 3. <b>Registration</b> — <c>mcpServers.sanduhr</c> in the chosen CC home's
///    <c>.claude.json</c>, with the same config-write safety as the statusline:
///    re-read, mutate ONLY the owned key, timestamped backup, temp + atomic
///    move; a malformed config is never overwritten. Deregistration only
///    removes an entry that points at Sanduhr's own launcher.
/// </summary>
public sealed class McpIntegrationInstaller
{
    private static readonly string[] KnownHomeNames = { ".claude", ".claude-personal" };

    private readonly string _homeDir;
    private readonly string _sanduhrDir;
    private readonly Action<string>? _logBestEffort;

    /// <param name="homeDir">User-profile dir the CC homes live under (test-injectable).</param>
    /// <param name="sanduhrDir">The Sanduhr app-data dir (<c>%APPDATA%\Sanduhr</c> in app code).</param>
    public McpIntegrationInstaller(string homeDir, string sanduhrDir, Action<string>? logBestEffort = null)
    {
        _homeDir = homeDir;
        _sanduhrDir = sanduhrDir;
        _logBestEffort = logBestEffort;
    }

    public string VersionsDir => Path.Combine(_sanduhrDir, "mcp");
    public string LauncherPath => Path.Combine(_sanduhrDir, "bin", "sanduhr-mcp.cmd");

    public string ConfigPathFor(string ccHomeName)
        => Path.Combine(_homeDir, ccHomeName, ".claude.json");

    /// <summary>CC homes that exist under the profile — same closed set as the
    /// statusline installer; the consent dialog picks ONE.</summary>
    public IReadOnlyList<string> DetectCcHomes()
    {
        var found = new List<string>();
        foreach (var name in KnownHomeNames)
        {
            if (Directory.Exists(Path.Combine(_homeDir, name)))
                found.Add(name);
        }
        return found;
    }

    /// <summary>Copy the server build from <paramref name="sourceDir"/> into a
    /// freshly stamped version dir. Returns the new dir, or null on failure.</summary>
    public string? InstallServerFiles(string sourceDir)
    {
        try
        {
            string exe = Path.Combine(sourceDir, "sanduhr-mcp.exe");
            if (!File.Exists(exe))
            {
                _logBestEffort?.Invoke("mcp install aborted (sanduhr-mcp.exe not found in source)");
                return null;
            }
            string version = FileVersionInfo.GetVersionInfo(exe).FileVersion is { Length: > 0 } v ? v : "0";
            string stamp = File.GetLastWriteTimeUtc(exe).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(VersionsDir, $"v{version}-{stamp}");

            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file);
                string dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp server-files install failed ({e.GetType().Name})");
            return null;
        }
    }

    /// <summary>Point the launcher at <paramref name="versionDir"/>. Plain ASCII,
    /// no BOM (a BOM breaks cmd.exe's first line).</summary>
    public bool WriteLauncher(string versionDir)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LauncherPath)!);
            string exe = Path.Combine(versionDir, "sanduhr-mcp.exe");
            string content =
                "@echo off\r\n" +
                "rem Sanduhr MCP launcher - rewritten by the Sanduhr widget on every update.\r\n" +
                "rem Version-dir indirection solves update-under-lock: a new version lands in\r\n" +
                "rem a new folder while running Claude Code sessions keep the old one pinned.\r\n" +
                $"\"{exe}\" %*\r\n";
            File.WriteAllText(LauncherPath, content);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp launcher write failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>Best-effort removal of version dirs other than
    /// <paramref name="keepDir"/>. A dir pinned by a running session refuses
    /// deletion — skipped, retried on the next install / health-check.</summary>
    public void PruneOldVersions(string? keepDir)
    {
        try
        {
            if (!Directory.Exists(VersionsDir))
                return;
            foreach (var dir in Directory.GetDirectories(VersionsDir))
            {
                if (keepDir is not null && string.Equals(dir, keepDir, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Pinned by a live session — exactly what the design expects.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp prune failed ({e.GetType().Name})");
        }
    }

    /// <summary>Fresh files + launcher + prune, in order — the install core and
    /// the app-start health-check body. Returns the active version dir or null.</summary>
    public string? RefreshInstall(string sourceDir)
    {
        string? dir = InstallServerFiles(sourceDir);
        if (dir is null)
            return null;
        if (!WriteLauncher(dir))
            return null;
        PruneOldVersions(dir);
        return dir;
    }

    /// <summary>Full local-file removal: the launcher and every version dir
    /// (pinned dirs skipped, best-effort — a dangling launcher pointing at a
    /// deleted dir just makes Claude Code report the server as failed, and the
    /// registration is removed separately by <see cref="Deregister"/>).</summary>
    public void RemoveIntegrationFiles()
    {
        try
        {
            if (File.Exists(LauncherPath))
                File.Delete(LauncherPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp launcher delete failed ({e.GetType().Name})");
        }
        PruneOldVersions(keepDir: null);
        try
        {
            if (Directory.Exists(VersionsDir) && Directory.GetDirectories(VersionsDir).Length == 0)
                Directory.Delete(VersionsDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Leftover empty dir is cosmetic.
        }
    }

    /// <summary>Write <c>mcpServers.sanduhr</c> into the chosen home's
    /// .claude.json. Refuses (writes nothing) when the existing file cannot be
    /// round-tripped. Re-owns an existing sanduhr entry — last explicit install
    /// wins — and reports what that entry pointed at.</summary>
    public McpRegisterResult Register(string ccHomeName)
    {
        string path = ConfigPathFor(ccHomeName);
        JsonObject root;
        try
        {
            if (File.Exists(path))
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject parsed)
                {
                    _logBestEffort?.Invoke("mcp register aborted (.claude.json not an object)");
                    return new McpRegisterResult(false, null);
                }
                root = parsed;
            }
            else
            {
                root = new JsonObject();
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp register aborted ({e.GetType().Name})");
            return new McpRegisterResult(false, null);
        }

        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        string? prior = null;
        if (servers["sanduhr"] is JsonObject existing)
            prior = (string?)existing["command"];

        servers["sanduhr"] = new JsonObject
        {
            ["command"] = LauncherPath,
            ["args"] = new JsonArray(),
        };
        bool ok = WriteConfigSafely(path, root, "register");
        return new McpRegisterResult(ok, prior);
    }

    /// <summary>Remove <c>mcpServers.sanduhr</c> — only when it points at a
    /// Sanduhr launcher/binary. Foreign entries are left untouched; true means
    /// "nothing of ours remains registered".</summary>
    public bool Deregister(string ccHomeName)
    {
        string path = ConfigPathFor(ccHomeName);
        try
        {
            if (!File.Exists(path))
                return true;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
                return true;
            if (root["mcpServers"] is not JsonObject servers || !IsOurs(servers["sanduhr"]))
                return true;
            servers.Remove("sanduhr");
            return WriteConfigSafely(path, root, "deregister");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp deregister failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>True when the home's .claude.json carries Sanduhr's entry.</summary>
    public bool IsRegistered(string ccHomeName)
    {
        try
        {
            string path = ConfigPathFor(ccHomeName);
            if (!File.Exists(path))
                return false;
            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                && root["mcpServers"] is JsonObject servers
                && IsOurs(servers["sanduhr"]);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsOurs(JsonNode? entry)
    {
        try
        {
            return entry is JsonObject o
                && (string?)o["command"] is { } cmd
                && cmd.Contains("sanduhr-mcp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool WriteConfigSafely(string path, JsonObject root, string operation)
    {
        try
        {
            if (File.Exists(path))
            {
                string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Copy(path, path + ".sanduhr-backup-" + stamp, overwrite: true);
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp {operation} write failed ({e.GetType().Name})");
            return false;
        }
    }
}
