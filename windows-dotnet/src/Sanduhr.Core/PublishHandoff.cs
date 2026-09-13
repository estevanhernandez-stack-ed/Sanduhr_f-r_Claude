using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The file-based handoff between <c>sanduhr-mcp</c>'s <c>publish_usage</c>
/// tool and the widget. The MCP server is credential-free and network-free by
/// construction (its csproj cannot compile <see cref="CredentialStore"/> or an
/// HTTP client — a pinned trust boundary), so it never posts anything itself:
/// it drops a request file next to the snapshot and the widget's tick loop
/// runs the publisher, writes the result file, and deletes the request.
///
/// The MCP project mirrors the two file names and the <c>id</c>/<c>date</c>
/// field names literally (it cannot link this file without widening its
/// allowlist); a test on each side pins them.
/// </summary>
public static class PublishHandoff
{
    public const string RequestFileName = "publish-request.json";
    public const string ResultFileName = "publish-result.json";

    /// <summary>Requests older than this are ignored and cleared — a request
    /// left behind by a long-dead session must not fire days later.</summary>
    public static readonly TimeSpan MaxRequestAge = TimeSpan.FromHours(24);

    public sealed record Request(string Id, DateOnly Date, DateTimeOffset RequestedAt);

    /// <summary>Parse the request file. Null when missing, unreadable, malformed,
    /// or expired (an expired file is deleted so it cannot linger).</summary>
    public static Request? TryReadRequest(string path, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o)
            {
                TryDelete(path);
                return null;
            }
            string? id = (string?)o["id"];
            string? dateText = (string?)o["date"];
            string? requestedText = (string?)o["requested_at"];
            if (string.IsNullOrEmpty(id)
                || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !DateTimeOffset.TryParse(requestedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var requestedAt))
            {
                TryDelete(path);
                return null;
            }
            if (now - requestedAt > MaxRequestAge)
            {
                TryDelete(path);
                return null;
            }
            return new Request(id, date, requestedAt);
        }
        catch (JsonException)
        {
            TryDelete(path);   // malformed = never valid; clear it so it cannot linger
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Write the result the MCP tool is polling for (atomic temp+swap)
    /// and clear the request. <paramref name="payload"/> is the typed tool result
    /// (status/reason/remedy/...) — the same shape every other tool returns.</summary>
    public static void WriteResult(string resultPath, string requestPath, string requestId, JsonObject payload)
    {
        var root = new JsonObject
        {
            ["id"] = requestId,
            ["completed_at"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["result"] = payload,
        };
        string tmp = resultPath + ".tmp";
        try
        {
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            File.Move(tmp, resultPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(tmp);
        }
        TryDelete(requestPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
