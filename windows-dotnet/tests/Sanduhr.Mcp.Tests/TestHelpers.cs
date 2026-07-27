using System.Globalization;
using System.Text.Json.Nodes;
using Sanduhr.Mcp;

namespace Sanduhr.Mcp.Tests;

/// <summary>Self-deleting temp dir (mirrors Sanduhr.Tests' TempDir — duplicated
/// because this project deliberately references only Sanduhr.Mcp).</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "sanduhr-mcp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best-effort */ }
    }
}

internal static class Helpers
{
    public static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    public static string Iso(DateTimeOffset t)
        => t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    /// <summary>A fresh ok snapshot: five_hour 42% (resets +3h), seven_day 62%
    /// (resets +5d), routines 3/15 (no reset instant).</summary>
    public static string OkSnapshotJson(DateTimeOffset capturedAt) => $$"""
        {"schema_version":1,"writer_version":"3.4.0","captured_at":"{{Iso(capturedAt)}}",
         "account_ref":"d6ab2208","plan":"Max 20x","status":"ok","error_kind":null,
         "tiers":[
           {"key":"five_hour","utilization":42,"resets_at":"{{Iso(capturedAt.AddHours(3))}}","used":null,"limit":null},
           {"key":"seven_day","utilization":62,"resets_at":"{{Iso(capturedAt.AddDays(5))}}","used":null,"limit":null},
           {"key":"routines","utilization":20,"resets_at":null,"used":3,"limit":15}
         ]}
        """;

    public static McpConfig Config(string snapshotPath, params (string Name, string Path)[] roots) => new()
    {
        SnapshotPath = snapshotPath,
        VaultDir = Path.Combine(Path.GetDirectoryName(snapshotPath)!, "vault"),
        ConsentedRoots = roots,
        RootsFound = roots.Select(r => r.Name).ToList(),
    };

    /// <summary>One assistant event line in CC session-log shape.</summary>
    public static string EventLine(DateTimeOffset ts, string? model, long input, long output, string? cwd)
    {
        var msg = new JsonObject
        {
            ["usage"] = new JsonObject { ["input_tokens"] = input, ["output_tokens"] = output },
        };
        if (model is not null) msg["model"] = model;
        var d = new JsonObject
        {
            ["type"] = "assistant",
            ["timestamp"] = ts.ToString("o"),
            ["message"] = msg,
        };
        if (cwd is not null) d["cwd"] = cwd;
        return d.ToJsonString();
    }

    /// <summary>Create a CC-root shaped tree: root/projects/{proj}/session.jsonl.</summary>
    public static string WriteSessionLog(string rootPath, string projectDirName, params string[] lines)
    {
        string dir = Path.Combine(rootPath, "projects", projectDirName);
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllLines(file, lines);
        return file;
    }

    public static JsonObject Parse(JsonObject result) => result;
}
