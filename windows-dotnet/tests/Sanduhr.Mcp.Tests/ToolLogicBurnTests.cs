using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

public class ToolLogicBurnTests
{
    private static ToolLogic Logic(TempDir tmp, params (string Name, string Path)[] roots)
        => new(Config(Path.Combine(tmp.Path, "snapshot.json"), roots), () => Now);

    [Fact]
    public void Invalid_window_is_a_typed_result_not_a_protocol_error()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp).BuildBurn(3, fullPaths: false);
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("invalid_params", (string?)r["reason"]);
    }

    [Fact]
    public void Zero_consented_roots_is_disabled_with_remedy()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp).BuildBurn(7, fullPaths: false);
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("disabled", (string?)r["reason"]);
        Assert.Contains("consent", (string?)r["remedy"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Results_are_keyed_per_root_with_basenames_by_default()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        string work = Path.Combine(tmp.Path, ".claude");
        WriteSessionLog(personal, "c--sanduhr",
            EventLine(Now.AddHours(-1), "claude-fable-5", 100, 200, @"C:\Users\estev\Projects\Sanduhr"),
            EventLine(Now.AddHours(-2), "claude-sonnet-5", 10, 20, @"C:\Users\estev\Projects\Sanduhr"));
        WriteSessionLog(work, "c--wbp",
            EventLine(Now.AddHours(-1), "claude-opus-4", 500, 500, @"C:\Users\estev\Projects\Marcus\wbp"));

        var r = Logic(tmp, (".claude", work), (".claude-personal", personal)).BuildBurn(7, fullPaths: false);

        Assert.Equal("ok", (string?)r["status"]);
        var roots = (JsonArray)r["roots"]!;
        Assert.Equal(2, roots.Count);

        var workRoot = (JsonObject)roots[0]!;
        Assert.Equal(".claude", (string?)workRoot["root"]);
        Assert.Equal(1000, (long)workRoot["total_tokens"]!.GetValue<long>());
        Assert.Equal("wbp", (string?)((JsonObject)((JsonArray)workRoot["projects"]!)[0]!)["name"]);

        var personalRoot = (JsonObject)roots[1]!;
        Assert.Equal(330, (long)personalRoot["total_tokens"]!.GetValue<long>());
        // Tenant wall: basename only — no username, no estate paths.
        Assert.Equal("Sanduhr", (string?)((JsonObject)((JsonArray)personalRoot["projects"]!)[0]!)["name"]);
        Assert.DoesNotContain("estev", r.ToJsonString());
    }

    [Fact]
    public void Full_paths_is_explicit_opt_in()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(personal, "c--sanduhr",
            EventLine(Now.AddHours(-1), "claude-fable-5", 1, 1, @"C:\Users\estev\Projects\Sanduhr"));
        var r = Logic(tmp, (".claude-personal", personal)).BuildBurn(7, fullPaths: true);
        var name = (string?)((JsonObject)((JsonArray)((JsonObject)((JsonArray)r["roots"]!)[0]!)["projects"]!)[0]!)["name"];
        Assert.Equal(@"C:\Users\estev\Projects\Sanduhr", name);
    }

    [Fact]
    public void Window_filter_excludes_old_events_and_counts_files()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(personal, "c--old",
            EventLine(Now.AddDays(-3), "claude-fable-5", 999, 0, @"C:\p\old"));
        WriteSessionLog(personal, "c--new",
            EventLine(Now.AddHours(-1), "claude-fable-5", 100, 0, @"C:\p\new"));

        var r = Logic(tmp, (".claude-personal", personal)).BuildBurn(1, fullPaths: false);
        var root = (JsonObject)((JsonArray)r["roots"]!)[0]!;
        Assert.Equal(100, (long)root["total_tokens"]!.GetValue<long>());
        Assert.Equal(1, (int)r["window_days"]!.GetValue<int>());
        Assert.True(r["files_scanned"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public void Events_without_cwd_stay_visible_as_unknown()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(personal, "c--x",
            EventLine(Now.AddHours(-1), "claude-fable-5", 40, 2, null));
        var r = Logic(tmp, (".claude-personal", personal)).BuildBurn(7, fullPaths: false);
        var proj = (JsonObject)((JsonArray)((JsonObject)((JsonArray)r["roots"]!)[0]!)["projects"]!)[0]!;
        Assert.Equal("(unknown)", (string?)proj["name"]);
        Assert.Equal(42, (long)proj["tokens"]!.GetValue<long>());
    }

    [Fact]
    public void Every_response_names_the_roots_it_covered()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        Directory.CreateDirectory(personal);
        var r = Logic(tmp, (".claude-personal", personal)).BuildBurn(7, fullPaths: false);
        var scanned = (JsonArray)r["roots_scanned"]!;
        Assert.Single(scanned);
        Assert.Equal(".claude-personal", (string?)scanned[0]);
    }
}
