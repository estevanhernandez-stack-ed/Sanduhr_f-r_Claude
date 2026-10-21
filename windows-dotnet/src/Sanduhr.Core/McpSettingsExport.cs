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
    /// exception type name only.
    /// </summary>
    public static void Sync(JsonObject settingsRoot, string path, Action<string>? log = null)
    {
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
                File.Move(temp, path, overwrite: true);
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
