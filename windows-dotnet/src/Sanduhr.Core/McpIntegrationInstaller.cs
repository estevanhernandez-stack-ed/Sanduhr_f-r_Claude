using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text;
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
///    <c>.claude.json</c>, written through the shared splice engine
///    (<see cref="JsonEdit"/> + <see cref="ConfigCommit"/>): only the owned key's bytes
///    change, the rest of the file stays byte for byte; the write is
///    compare-and-swap (re-read, abort and plan again when Claude Code wrote
///    meanwhile). A copy of the original bytes goes to <c>&lt;sanduhrDir&gt;\backups</c>
///    and never beside the Claude Code file; a malformed config is never
///    overwritten. Deregistration only removes an entry that points at Sanduhr's own
///    launcher, from every file Claude Code may read for the home.
/// </summary>
public sealed class McpIntegrationInstaller
{
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

    /// <summary>The .claude.json Claude Code reads for a home: one placement rule,
    /// <see cref="ClaudeCodeHomes.ConfigFile"/>. Any home but <c>.claude</c> keeps it
    /// inside the home; for <c>.claude</c> the signed-in file wins, then the newer
    /// inner one, else the one beside the home at <c>%USERPROFILE%\.claude.json</c>.</summary>
    public string ConfigPathFor(string ccHomeName)
        => ClaudeCodeHomes.ConfigFile(Path.Combine(_homeDir, ccHomeName), _homeDir);

    /// <summary>Every <c>.claude.json</c> that may hold Sanduhr's entry for the home. One file for any
    /// home but <c>.claude</c>; for <c>.claude</c> both the file beside it and the stub inside it, because
    /// which one <see cref="ClaudeCodeHomes.ConfigFile"/> picks changes with sign-in and file age, and an
    /// earlier build may have written to the other.</summary>
    public IReadOnlyList<string> CandidateConfigPaths(string ccHomeName)
    {
        string dir = Path.Combine(_homeDir, ccHomeName);
        string chosen = ConfigPathFor(ccHomeName);
        var list = new List<string> { chosen };
        if (ClaudeCodeFolders.Same(dir, Path.Combine(_homeDir, ClaudeCodeHomes.DefaultName)))
        {
            foreach (var other in new[] { Path.Combine(_homeDir, ".claude.json"), Path.Combine(dir, ".claude.json") })
                if (!list.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase)))
                    list.Add(other);
        }
        return list;
    }

    private ConfigCommit Commit => new(Path.Combine(_sanduhrDir, "backups"));

    /// <summary>CC homes under the profile, as <see cref="ClaudeCodeHomes.Discover"/>
    /// finds them — same set as the statusline installer; the consent dialog picks ONE.</summary>
    public IReadOnlyList<string> DetectCcHomes()
        => ClaudeCodeHomes.Discover(_homeDir, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));

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

            // Same version + same build stamp = the copy that is already there. Leave
            // it alone: it may be pinned by a running Claude Code session, and the
            // app-start refresh would otherwise re-copy the server on every launch.
            if (File.Exists(Path.Combine(target, "sanduhr-mcp.exe")))
                return target;

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
        string? prior = null;
        bool refuse = false;

        byte[]? Plan(byte[]? original)
        {
            prior = null;
            refuse = false;
            if (original is null)
            {
                return Encoding.UTF8.GetBytes(new JsonObject
                {
                    ["mcpServers"] = new JsonObject { ["sanduhr"] = OurEntryNode() },
                }.ToJsonString(IndentedJson) + "\n");
            }
            if (!JsonEdit.IsObject(original))
            {
                refuse = true;
                return null;
            }
            var top = JsonEdit.TopObject(original);
            byte[] result;
            if (top.Member("mcpServers") is { } serversMember && IsObjectAt(original, serversMember.ValueStart))
            {
                var servers = JsonEdit.Object(original, serversMember.ValueStart);
                if (servers.Member("sanduhr") is { } existing)
                    prior = CommandOf(JsonEdit.Text(original, existing));
                result = JsonEdit.Set(original, servers, "sanduhr", OurEntry()).Bytes;
            }
            else if (top.Member("mcpServers") is not null)
            {
                // An mcpServers that isn't an object is someone else's value: never replace it.
                refuse = true;
                return null;
            }
            else
            {
                // No mcpServers yet.
                result = JsonEdit.Set(original, top, "mcpServers",
                    JsonEdit.Value.Obj(new JsonEdit.Pair("sanduhr", OurEntry()))).Bytes;
            }
            return result.AsSpan().SequenceEqual(original) ? null : result;
        }

        try
        {
            var outcome = Commit.RetryCommit(path, Plan, backup: true);
            if (refuse)
            {
                _logBestEffort?.Invoke("mcp register aborted (.claude.json not an object)");
                return new McpRegisterResult(false, null);
            }
            bool ok = outcome != CommitOutcome.Conflict;
            if (!ok)
                _logBestEffort?.Invoke("mcp register gave up (.claude.json kept changing)");
            return new McpRegisterResult(ok, ok ? prior : null);
        }
        catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp register aborted ({e.GetType().Name})");
            return new McpRegisterResult(false, null);
        }
    }

    /// <summary>Remove <c>mcpServers.sanduhr</c> — only when it points at a
    /// Sanduhr launcher/binary — from every file Claude Code may read for the home
    /// (<see cref="CandidateConfigPaths"/>: for <c>.claude</c> both the file beside
    /// the home and the one inside it, since a registration written by an earlier
    /// build can sit in either). Foreign entries are left untouched; true means
    /// "nothing of ours remains registered".</summary>
    public bool Deregister(string ccHomeName)
    {
        bool all = true;
        foreach (var path in CandidateConfigPaths(ccHomeName))
            all &= DeregisterFile(path);
        return all;
    }

    private bool DeregisterFile(string path)
    {
        bool failed = false;

        byte[]? Plan(byte[]? original)
        {
            failed = false;
            if (original is null)
                return null;
            if (!JsonEdit.IsObject(original))
            {
                // Valid JSON that is not an object is not ours to touch; broken JSON is a failure.
                try { JsonNode.Parse(Utf8Text(original)); }
                catch (JsonException) { failed = true; }
                return null;
            }
            var top = JsonEdit.TopObject(original);
            if (top.Member("mcpServers") is not { } serversMember || !IsObjectAt(original, serversMember.ValueStart))
                return null;
            var servers = JsonEdit.Object(original, serversMember.ValueStart);
            if (servers.Member("sanduhr") is not { } entry || !IsOurs(original, entry))
                return null;
            return JsonEdit.Remove(original, servers, "sanduhr");
        }

        try
        {
            var outcome = Commit.RetryCommit(path, Plan, backup: true);
            if (failed)
            {
                _logBestEffort?.Invoke("mcp deregister failed (JsonException)");
                return false;
            }
            if (outcome == CommitOutcome.Conflict)
            {
                _logBestEffort?.Invoke("mcp deregister gave up (.claude.json kept changing)");
                return false;
            }
            return true;
        }
        catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"mcp deregister failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>The reset path: deregister the server from every home, then delete the launcher and the
    /// version dirs, but only when every deregister succeeded. A failed entry keeps its files, so the
    /// registration Claude Code still holds keeps working until the user removes it by hand.</summary>
    public bool UndoAll(IEnumerable<string> ccHomeNames)
    {
        bool all = true;
        foreach (var home in ccHomeNames)
            all &= Deregister(home);
        if (all)
            RemoveIntegrationFiles();
        return all;
    }

    /// <summary>True when any file Claude Code may read for the home carries Sanduhr's entry.</summary>
    public bool IsRegistered(string ccHomeName)
    {
        foreach (var path in CandidateConfigPaths(ccHomeName))
        {
            try
            {
                if (Commit.Read(path) is not { } bytes || !JsonEdit.IsObject(bytes))
                    continue;
                var top = JsonEdit.TopObject(bytes);
                if (top.Member("mcpServers") is { } sm && IsObjectAt(bytes, sm.ValueStart)
                    && JsonEdit.Object(bytes, sm.ValueStart).Member("sanduhr") is { } entry
                    && IsOurs(bytes, entry))
                    return true;
            }
            catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
            {
                // An unreadable file does not count as registered.
            }
        }
        return false;
    }

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private static string Utf8Text(byte[] bytes)
    {
        int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip);
    }

    private static bool IsObjectAt(byte[] bytes, int valueStart)
        => valueStart < bytes.Length && bytes[valueStart] == (byte)'{';

    private JsonEdit.Value OurEntry()
        => JsonEdit.Value.Obj(
            new JsonEdit.Pair("command", JsonEdit.Value.Str(LauncherPath)),
            new JsonEdit.Pair("args", JsonEdit.Value.Arr()));

    private JsonNode OurEntryNode()
        => new JsonObject { ["command"] = LauncherPath, ["args"] = new JsonArray() };

    private static string? CommandOf(byte[] entryBytes)
    {
        try
        {
            return JsonNode.Parse(Utf8Text(entryBytes)) is JsonObject o ? (string?)o["command"] : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static bool IsOurs(byte[] bytes, JsonEdit.Member entry)
    {
        try
        {
            return CommandOf(JsonEdit.Text(bytes, entry)) is { } cmd
                && cmd.Contains("sanduhr-mcp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
