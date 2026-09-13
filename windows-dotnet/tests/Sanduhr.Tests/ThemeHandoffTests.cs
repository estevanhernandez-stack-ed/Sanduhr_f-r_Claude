using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class ThemeHandoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 20, 0, 0, TimeSpan.Zero);

    private static string WriteRequest(TempDir tmp, string json)
    {
        string p = Path.Combine(tmp.Path, ThemeHandoff.RequestFileName);
        File.WriteAllText(p, json);
        return p;
    }

    [Fact]
    public void File_names_are_the_ones_the_mcp_project_mirrors()
    {
        Assert.Equal("theme-request.json", ThemeHandoff.RequestFileName);
        Assert.Equal("theme-result.json", ThemeHandoff.ResultFileName);
    }

    [Fact]
    public void Request_round_trips_with_defaults_for_omitted_fields()
    {
        using var tmp = new TempDir();
        string p = WriteRequest(tmp, """{"id":"abc","requested_at":"2026-09-13T19:59:00Z","theme":{"name":"Nebula","accent":"#ff00ff"}}""");

        var r = ThemeHandoff.TryReadRequest(p, Now);

        Assert.NotNull(r);
        Assert.Equal("abc", r!.Id);
        Assert.Equal("Nebula", (string?)r.Theme["name"]);
        Assert.Null(r.SaveAs);
        Assert.True(r.Apply);
        Assert.True(File.Exists(p));   // reading never clears a valid request
    }

    [Fact]
    public void Request_carries_save_as_and_apply_false()
    {
        using var tmp = new TempDir();
        string p = WriteRequest(tmp, """{"id":"abc","requested_at":"2026-09-13T19:59:00Z","theme":{"name":"N"},"save_as":"nebula-2","apply":false}""");
        var r = ThemeHandoff.TryReadRequest(p, Now)!;
        Assert.Equal("nebula-2", r.SaveAs);
        Assert.False(r.Apply);
    }

    [Fact]
    public void Expired_request_is_deleted_and_ignored()
    {
        using var tmp = new TempDir();
        string p = WriteRequest(tmp, """{"id":"old","requested_at":"2026-09-13T19:00:00Z","theme":{}}""");
        Assert.Null(ThemeHandoff.TryReadRequest(p, Now));   // 60 minutes old, window is 10
        Assert.False(File.Exists(p));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[1]")]
    [InlineData("""{"id":"x","requested_at":"2026-09-13T19:59:00Z"}""")]          // no theme
    [InlineData("""{"id":"x","requested_at":"2026-09-13T19:59:00Z","theme":"s"}""")] // theme not an object
    [InlineData("""{"requested_at":"2026-09-13T19:59:00Z","theme":{}}""")]         // no id
    public void Malformed_request_is_deleted_and_ignored(string json)
    {
        using var tmp = new TempDir();
        string p = WriteRequest(tmp, json);
        Assert.Null(ThemeHandoff.TryReadRequest(p, Now));
        Assert.False(File.Exists(p));
    }

    [Fact]
    public void Missing_request_is_null()
    {
        using var tmp = new TempDir();
        Assert.Null(ThemeHandoff.TryReadRequest(Path.Combine(tmp.Path, ThemeHandoff.RequestFileName), Now));
    }

    [Fact]
    public void WriteResult_wraps_the_payload_and_clears_the_request()
    {
        using var tmp = new TempDir();
        string req = WriteRequest(tmp, """{"id":"abc","requested_at":"2026-09-13T19:59:00Z","theme":{}}""");
        string res = Path.Combine(tmp.Path, ThemeHandoff.ResultFileName);

        ThemeHandoff.WriteResult(res, req, "abc", new JsonObject { ["status"] = "applied", ["key"] = "nebula" });

        Assert.False(File.Exists(req));
        Assert.False(File.Exists(res + ".tmp"));
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(res))!;
        Assert.Equal("abc", (string?)root["id"]);
        Assert.Equal("applied", (string?)root["result"]!["status"]);
        Assert.NotNull(root["completed_at"]);
    }

    [Theory]
    [InlineData("nebula", true)]
    [InlineData("nebula-2", true)]
    [InlineData("2fast", true)]
    [InlineData("-nebula", false)]
    [InlineData("Nebula", false)]
    [InlineData("neb ula", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Key_rule(string? key, bool valid)
    {
        Assert.Equal(valid, ThemeHandoff.IsValidKey(key));
        Assert.False(ThemeHandoff.IsValidKey(new string('a', 41)));
    }

    [Theory]
    [InlineData("Nebula", "nebula")]
    [InlineData("  Deep Space 9 ", "deep-space-9")]
    [InlineData("Sanduhr für Claude", "sanduhr-f-r-claude")]
    [InlineData("!!!", "theme")]
    public void Slugify_matches_the_themes_tab_rule(string name, string key)
    {
        Assert.Equal(key, ThemeHandoff.Slugify(name));
        Assert.True(ThemeHandoff.IsValidKey(ThemeHandoff.Slugify(name)));
    }
}
