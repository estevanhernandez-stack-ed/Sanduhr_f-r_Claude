using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The projection of <c>settings.json</c> that sanduhr-mcp reads (spec Plan B, B.2): consent per Claude Code home and
/// publish readiness, written to <c>%USERPROFILE%\.sanduhr\mcp-settings.json</c> because settings.json itself stays in
/// AppData, where a Store build keeps it private. It exists only while <c>mcp_enabled</c> is true. Never the endpoint URL,
/// never a token: the server learns whether publishing could work, not where it goes.
/// </summary>
public static class McpSettingsExport
{
    /// <summary>The projection, or null when the MCP integration is not enabled (the file must not exist then).</summary>
    public static JsonObject? Project(JsonObject settingsRoot)
    {
        if (!ReadBool(settingsRoot["mcp_enabled"]))
            return null;

        var roots = new JsonObject();
        if (settingsRoot["mcp_roots"] is JsonObject map)
        {
            foreach (var (name, node) in map)
                roots[name] = ReadBool(node);
        }

        bool enabled, hasEndpoint, credentialReady;
        if (settingsRoot["publish"] is JsonObject group)
        {
            enabled = ReadBool(group["enabled"]);
            hasEndpoint = !string.IsNullOrWhiteSpace(ReadString(group["endpoint_url"]));
            bool noAuth = string.Equals(ReadString(group["auth_scheme"])?.Trim(), "none", StringComparison.OrdinalIgnoreCase);
            credentialReady = ReadBool(group["token_stored"]) || noAuth;
        }
        else if (settingsRoot["publish_626"] is JsonObject legacy)
        {
            // Pre-3.5 group: it described the 626 Labs endpoint, so the endpoint counts as set.
            enabled = ReadBool(legacy["enabled"]);
            hasEndpoint = true;
            credentialReady = ReadBool(legacy["key_stored"]);
        }
        else
        {
            enabled = hasEndpoint = credentialReady = false;
        }

        return new JsonObject
        {
            ["schema_version"] = 1,
            ["mcp_roots"] = roots,
            ["publish"] = new JsonObject
            {
                ["enabled"] = enabled,
                ["has_endpoint"] = hasEndpoint,
                ["credential_ready"] = credentialReady,
            },
        };
    }

    /// <summary>
    /// Bring the file at <paramref name="path"/> in line with the projection: deleted when the projection is null, else
    /// written only when its bytes differ, atomically (temp sibling, then replace). Never throws; a failure is logged by
    /// exception type name only. sanduhr-mcp may hold the file open for a moment, so the replace is retried up to three
    /// times (about 60 ms apart); if it still fails the old file is deleted when possible, so the server fails closed
    /// (missing means nothing is consented) rather than keeping stale consent.
    /// </summary>
    /// <param name="replace">Test seam for the atomic replace: (temp, target). Defaults to <c>File.Move(overwrite)</c>.</param>
    public static void Sync(JsonObject settingsRoot, string path, Action<string>? log = null,
        Action<string, string>? replace = null)
    {
        replace ??= (from, to) => File.Move(from, to, overwrite: true);
        try
        {
            var projection = Project(settingsRoot);
            if (projection is null)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            byte[] bytes = new UTF8Encoding(false).GetBytes(projection.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            if (File.Exists(path))
            {
                try
                {
                    if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                        return;
                }
                catch (IOException) { /* unreadable: rewrite below */ }
            }

            string dir = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(dir);
            string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                const int retries = 3;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        replace(temp, path);
                        break;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        if (attempt < retries)
                        {
                            Thread.Sleep(20 * (attempt + 1));
                            continue;
                        }
                        FailClosed(path, e, log);
                        break;
                    }
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"mcp-settings: sync failed ({e.GetType().Name})");
        }
    }

    /// <summary>The replace failed for good: take the old file away so the server reads "missing" (nothing consented, publish
    /// off) instead of a projection that no longer matches settings.json. One log line, exception type names only.</summary>
    private static void FailClosed(string path, Exception cause, Action<string>? log)
    {
        string outcome;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            outcome = "stale file removed";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            outcome = $"stale file kept ({e.GetType().Name})";
        }
        log?.Invoke($"mcp-settings: sync failed ({cause.GetType().Name}), {outcome}");
    }

    private static bool ReadBool(JsonNode? node)
    {
        try { return node?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    private static string? ReadString(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch { return null; }
    }
}
