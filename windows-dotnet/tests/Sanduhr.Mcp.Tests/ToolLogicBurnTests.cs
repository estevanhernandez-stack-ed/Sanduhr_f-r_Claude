using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

public class ToolLogicBurnTests
{
    private static ToolLogic Logic(TempDir tmp, params (string Name, string Path)[] roots)
        => new(Config(Path.Combine(tmp.Path, "snapshot.json"), roots), () => Now);

    [Fact]
    public void Consented_agents_report_their_project_totals_from_the_vault()
    {
        using var tmp = new TempDir();
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = Path.Combine(tmp.Path, "vault"),
            ConsentedRoots = Array.Empty<(string, string)>(),
            RootsFound = Array.Empty<string>(),
            ConsentedAgents = new[] { "codex" },
        };
        string day = DateOnly.FromDateTime(Now.LocalDateTime).ToString("yyyy-MM-dd");
        string month = day[..7];
        new Sanduhr.Core.VaultStore(cfg.VaultDir).SaveRollupShard("codex", month, new Sanduhr.Core.VaultRollupShard
        {
            Days = new Dictionary<string, Sanduhr.Core.VaultRollupDay>
            {
                [day] = new() { Total = 300, Input = 100, Output = 200, ByProject = new() { ["api~12345678"] = 300 } },
            },
        });

        var result = new ToolLogic(cfg, () => Now).BuildBurn(7, fullPaths: false);

        Assert.Equal("ok", result["status"]!.GetValue<string>());
        var agent = (JsonObject)((JsonArray)result["agents"]!)[0]!;
        Assert.Equal("codex", agent["agent"]!.GetValue<string>());
        Assert.Equal(300, agent["total_tokens"]!.GetValue<long>());
        Assert.Equal("api", (string?)((JsonObject)((JsonArray)agent["projects"]!)[0]!)["name"]);
        Assert.Equal("codex", (string?)((JsonArray)result["agents_scanned"]!)[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Agent_window_is_windowDays_calendar_days_ending_today(int windowDays)
    {
        using var tmp = new TempDir();
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = Path.Combine(tmp.Path, "vault"),
            ConsentedRoots = Array.Empty<(string, string)>(),
            RootsFound = Array.Empty<string>(),
            ConsentedAgents = new[] { "codex" },
        };
        var today = DateOnly.FromDateTime(Now.LocalDateTime);
        var outside = today.AddDays(-windowDays);
        var inside = today.AddDays(-(windowDays - 1));
        var store = new Sanduhr.Core.VaultStore(cfg.VaultDir);
        foreach (var g in new[] { outside, inside }.GroupBy(d => d.ToString("yyyy-MM")))
        {
            var days = new Dictionary<string, Sanduhr.Core.VaultRollupDay>();
            foreach (var d in g)
            {
                long t = d == inside ? 50 : 900;
                days[d.ToString("yyyy-MM-dd")] = new() { Total = t, Input = t, Output = 0, ByProject = new() { ["api~12345678"] = t } };
            }
            store.SaveRollupShard("codex", g.Key, new Sanduhr.Core.VaultRollupShard { Days = days });
        }

        var result = new ToolLogic(cfg, () => Now).BuildBurn(windowDays, fullPaths: false);

        var agent = (JsonObject)((JsonArray)result["agents"]!)[0]!;
        Assert.Equal(50, agent["total_tokens"]!.GetValue<long>());
        Assert.Equal(inside.ToString("yyyy-MM-dd"), (string?)result["agents_from"]);
    }

    [Fact]
    public void An_unreadable_vault_drops_the_agents_and_the_claude_roots_still_answer()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(personal, "c--sanduhr",
            EventLine(Now.AddHours(-1), "claude-fable-5", 100, 200, Path.Combine(tmp.Path, "Sanduhr")));
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = Path.Combine(tmp.Path, "vault"),
            ConsentedRoots = new[] { (".claude-personal", personal) },
            RootsFound = Array.Empty<string>(),
            ConsentedAgents = new[] { "codex" },
        };
        // Parseable but broken: a shard whose days are null fails the read itself, not the load.
        string month = DateOnly.FromDateTime(Now.LocalDateTime).ToString("yyyy-MM");
        Directory.CreateDirectory(Path.Combine(cfg.VaultDir, "codex"));
        File.WriteAllText(Path.Combine(cfg.VaultDir, "codex", $"rollups-{month}.json"), "{\"schema_version\":1,\"days\":null}");

        var result = new ToolLogic(cfg, () => Now).BuildBurn(7, fullPaths: false);

        Assert.Equal("ok", (string?)result["status"]);
        Assert.Empty((JsonArray)result["agents"]!);
        Assert.Equal("unreadable", (string?)result["agents_reason"]);
        var root = (JsonObject)((JsonArray)result["roots"]!)[0]!;
        Assert.Equal(300, root["total_tokens"]!.GetValue<long>());
    }

    [Fact]
    public void A_readable_vault_gives_no_agents_reason()
    {
        using var tmp = new TempDir();
        string root = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(root);
        var result = Logic(tmp, (".claude", root)).BuildBurn(7, fullPaths: false);
        Assert.Null(result["agents_reason"]);
    }

    [Fact]
    public void Without_agent_consent_no_agents_are_reported()
    {
        using var tmp = new TempDir();
        string root = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(root);
        var result = Logic(tmp, (".claude", root)).BuildBurn(7, fullPaths: false);
        Assert.Empty((JsonArray)result["agents"]!);
    }

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
        Assert.Contains("(Settings, Claude Code, Folders)", (string?)r["remedy"]);
        Assert.DoesNotContain("mcp_roots", (string?)r["remedy"]);
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
        // The work cwd lives under the test's own temp tree: ProjectDisplayName
        // names the nearest ENCLOSING git repo, so a fixture path pointing into a
        // real checkout would answer differently on a developer's disk than on CI.
        // The temp path still carries the username, which is exactly what the
        // DoesNotContain("estev") assertion below proves gets stripped.
        string workCwd = Path.Combine(tmp.Path, "Marcus", "wbp");
        WriteSessionLog(work, "c--wbp",
            EventLine(Now.AddHours(-1), "claude-opus-4", 500, 500, workCwd));

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
