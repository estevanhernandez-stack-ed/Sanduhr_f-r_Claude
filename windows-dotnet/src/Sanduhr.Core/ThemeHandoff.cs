using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The file-based handoff between <c>sanduhr-mcp</c>'s <c>propose_theme</c>
/// tool and the widget, the sibling of <see cref="PublishHandoff"/>. The
/// server lints a palette and drops a request file; the widget lints it again
/// (it is the authority), saves it under the themes folder, applies it when
/// asked, and answers through the result file. The server never writes a
/// theme itself.
///
/// The MCP project mirrors the two file names and the field names literally
/// (it cannot link this file without widening its allowlist); a test on each
/// side pins them.
/// </summary>
public static class ThemeHandoff
{
    public const string RequestFileName = "theme-request.json";
    public const string ResultFileName = "theme-result.json";

    /// <summary>A theme proposal is interactive. One that arrives after the
    /// session moved on must not restyle the widget later, so the window is
    /// short (publish's is 24 hours).</summary>
    public static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(10);

    /// <summary>The proposal: the palette as the agent wrote it (lint decides),
    /// an optional file key, and whether to apply after saving.</summary>
    public sealed record Request(string Id, JsonObject Theme, string? SaveAs, bool Apply, DateTimeOffset RequestedAt);

    /// <summary>Parse the request file. Null when missing, unreadable, malformed,
    /// or expired (an expired or malformed file is deleted so it cannot linger).</summary>
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
            string? requestedText = (string?)o["requested_at"];
            if (string.IsNullOrEmpty(id)
                || o["theme"] is not JsonObject theme
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
            string? saveAs = o["save_as"] is JsonValue sv && sv.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
            bool apply = true;
            if (o["apply"] is JsonValue av)
            {
                try { apply = av.GetValue<bool>(); }
                catch { apply = true; }
            }
            // Detach the theme so the caller owns it (a JsonNode has one parent).
            return new Request(id, (JsonObject)theme.DeepClone(), saveAs, apply, requestedAt);
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
    /// (status/reason/remedy/key/name/previous_key/saved_path/findings).</summary>
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

    /// <summary>The file-key rule shared by the Themes tab, the studio, and the
    /// handoff: lowercase, digits and hyphens, 1 to 40 characters, no leading
    /// hyphen. A proposal's <c>save_as</c> must already match; a display name is
    /// slugged into it.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 40)
            return false;
        if (!char.IsAsciiLetterLower(key[0]) && !char.IsAsciiDigit(key[0]))
            return false;
        foreach (char c in key)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
                return false;
        }
        return true;
    }

    /// <summary>Display name to file key: lowercase, runs of anything else become
    /// one hyphen, trimmed; "theme" when nothing survives. Same rule as the
    /// Themes tab's filename autofill.</summary>
    public static string Slugify(string name)
    {
        var chars = new List<char>(name.Length);
        bool pendingHyphen = false;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                if (pendingHyphen && chars.Count > 0)
                    chars.Add('-');
                pendingHyphen = false;
                chars.Add(c);
            }
            else
            {
                pendingHyphen = true;
            }
        }
        string s = new(chars.ToArray());
        if (s.Length > 40)
            s = s[..40].TrimEnd('-');
        return s.Length == 0 ? "theme" : s;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
