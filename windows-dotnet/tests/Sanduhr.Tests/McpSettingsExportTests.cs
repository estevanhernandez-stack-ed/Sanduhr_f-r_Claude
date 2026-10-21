using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class McpSettingsExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanduhr-mcpexp-" + Guid.NewGuid().ToString("N"));

    public McpSettingsExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Project_is_null_unless_mcp_enabled_is_true()
    {
        Assert.Null(McpSettingsExport.Project(Parse("{}")));
        Assert.Null(McpSettingsExport.Project(Parse("""{"mcp_enabled":false,"mcp_roots":{".claude":true}}""")));
        Assert.Null(McpSettingsExport.Project(Parse("""{"mcp_enabled":"yes"}""")));
    }

    [Fact]
    public void Project_copies_roots_as_bools_and_defaults_publish_to_off()
    {
        var p = McpSettingsExport.Project(Parse("""{"mcp_enabled":true,"mcp_roots":{".claude":true,".claude-work":false,"x":"true","y":null}}"""))!;
        Assert.Equal(1, (int)p["schema_version"]!);
        var roots = (JsonObject)p["mcp_roots"]!;
        Assert.True((bool)roots[".claude"]!);
        Assert.False((bool)roots[".claude-work"]!);
        Assert.False((bool)roots["x"]!);
        Assert.False((bool)roots["y"]!);
        var pub = (JsonObject)p["publish"]!;
        Assert.False((bool)pub["enabled"]!);
        Assert.False((bool)pub["has_endpoint"]!);
        Assert.False((bool)pub["credential_ready"]!);
    }

    [Fact]
    public void Project_publish_group_computes_readiness_and_never_carries_the_endpoint_or_a_token()
    {
        var p = McpSettingsExport.Project(Parse("""{"mcp_enabled":true,"publish":{"enabled":true,"endpoint_url":"https://secret.example/x","token_stored":true,"auth_scheme":"bearer"}}"""))!;
        var pub = (JsonObject)p["publish"]!;
        Assert.True((bool)pub["enabled"]!);
        Assert.True((bool)pub["has_endpoint"]!);
        Assert.True((bool)pub["credential_ready"]!);
        Assert.DoesNotContain("secret.example", p.ToJsonString());
    }

    [Theory]
    [InlineData("""{"enabled":true,"endpoint_url":"https://e","token_stored":false,"auth_scheme":" None "}""", true, true, true)]
    [InlineData("""{"enabled":true,"endpoint_url":"https://e","token_stored":false,"auth_scheme":"bearer"}""", true, true, false)]
    [InlineData("""{"enabled":true,"endpoint_url":"   ","token_stored":true}""", true, false, true)]
    [InlineData("""{"enabled":"x","endpoint_url":5}""", false, false, false)]
    public void Project_publish_readiness_cases(string group, bool enabled, bool hasEndpoint, bool ready)
    {
        var p = McpSettingsExport.Project(Parse("{\"mcp_enabled\":true,\"publish\":" + group + "}"))!;
        var pub = (JsonObject)p["publish"]!;
        Assert.Equal(enabled, (bool)pub["enabled"]!);
        Assert.Equal(hasEndpoint, (bool)pub["has_endpoint"]!);
        Assert.Equal(ready, (bool)pub["credential_ready"]!);
    }

    [Fact]
    public void Project_honors_the_legacy_publish_626_group_only_without_a_publish_group()
    {
        var legacy = McpSettingsExport.Project(Parse("""{"mcp_enabled":true,"publish_626":{"enabled":true,"key_stored":true}}"""))!;
        var pub = (JsonObject)legacy["publish"]!;
        Assert.True((bool)pub["enabled"]!);
        Assert.True((bool)pub["has_endpoint"]!);
        Assert.True((bool)pub["credential_ready"]!);

        var both = McpSettingsExport.Project(Parse("""{"mcp_enabled":true,"publish":{"enabled":false},"publish_626":{"enabled":true,"key_stored":true}}"""))!;
        Assert.False((bool)((JsonObject)both["publish"]!)["enabled"]!);
    }

    [Fact]
    public void Sync_writes_the_projection_and_skips_an_identical_rewrite()
    {
        var path = Path.Combine(_dir, "sub", "mcp-settings.json");
        var root = Parse("""{"mcp_enabled":true,"mcp_roots":{".claude":true}}""");
        McpSettingsExport.Sync(root, path);
        Assert.True(File.Exists(path));
        var first = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, first.AddMinutes(-5));
        var stamped = File.GetLastWriteTimeUtc(path);

        McpSettingsExport.Sync(root, path);
        Assert.Equal(stamped, File.GetLastWriteTimeUtc(path));   // same bytes: untouched

        root["mcp_roots"]![".claude"] = false;
        McpSettingsExport.Sync(root, path);
        Assert.NotEqual(stamped, File.GetLastWriteTimeUtc(path));
        Assert.False((bool)JsonNode.Parse(File.ReadAllText(path))!["mcp_roots"]![".claude"]!);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void Sync_deletes_the_file_when_the_integration_is_off_and_tolerates_none()
    {
        var path = Path.Combine(_dir, "mcp-settings.json");
        File.WriteAllText(path, "{}");
        McpSettingsExport.Sync(Parse("""{"mcp_enabled":false}"""), path);
        Assert.False(File.Exists(path));
        McpSettingsExport.Sync(Parse("{}"), path);   // nothing to delete, no throw
    }

    [Fact]
    public void Sync_never_throws_and_logs_the_exception_type_when_the_write_fails()
    {
        // The target path is a directory: the replace cannot succeed.
        var path = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "keep"), "x");
        var logs = new List<string>();
        McpSettingsExport.Sync(Parse("""{"mcp_enabled":true}"""), path, logs.Add);
        Assert.Single(logs);
        Assert.DoesNotContain(_dir, logs[0]);
    }
}
