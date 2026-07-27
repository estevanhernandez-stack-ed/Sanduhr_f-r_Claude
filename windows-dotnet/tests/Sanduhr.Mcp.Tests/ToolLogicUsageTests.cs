using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

public class ToolLogicUsageTests
{
    private static ToolLogic Logic(TempDir tmp, string? snapshotJson, DateTimeOffset now,
        params (string Name, string Path)[] roots)
    {
        string path = Path.Combine(tmp.Path, "snapshot.json");
        if (snapshotJson is not null)
            File.WriteAllText(path, snapshotJson);
        return new ToolLogic(Config(path, roots), () => now);
    }

    [Fact]
    public void Missing_snapshot_is_typed_no_data_with_remedy()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, null, Now).BuildUsage();
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("missing", (string?)r["reason"]);
        Assert.Contains("Sanduhr widget", (string?)r["remedy"]);
    }

    [Fact]
    public void Malformed_snapshot_is_typed_no_data()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, "{ not json", Now).BuildUsage();
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("malformed", (string?)r["reason"]);
    }

    [Fact]
    public void Newer_schema_major_is_refused_not_best_effort_parsed()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, """{"schema_version":2,"captured_at":"2026-07-26T11:59:00+00:00","status":"ok","tiers":[]}""", Now).BuildUsage();
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("schema_unsupported", (string?)r["reason"]);
        Assert.Contains("Update Sanduhr", (string?)r["remedy"]);
    }

    [Fact]
    public void Fresh_snapshot_maps_ok_with_tiers_account_and_derivations()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-2)), Now).BuildUsage();

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Null(r["reason"]?.GetValue<string>());
        Assert.Equal(120, (double)r["age_seconds"]!.GetValue<double>());
        Assert.Equal("active_account_only", (string?)r["scope"]);
        Assert.Equal("d6ab2208", (string?)r["account"]!["ref"]);
        Assert.Equal("Max 20x", (string?)r["account"]!["plan"]);

        var tiers = (JsonArray)r["tiers"]!;
        Assert.Equal(3, tiers.Count);

        var fiveHour = (JsonObject)tiers[0]!;
        Assert.Equal("Session (5hr)", (string?)fiveHour["label"]);
        Assert.Equal(42, (int)fiveHour["utilization_pct"]!.GetValue<int>());
        Assert.Equal(58, (int)fiveHour["headroom_pct"]!.GetValue<int>());
        Assert.False(fiveHour["reset_crossed"]!.GetValue<bool>());
        // resets in ~3h minus the 2m age: server does the clock math, not the agent
        double resetsIn = fiveHour["resets_in_seconds"]!.GetValue<double>();
        Assert.InRange(resetsIn, 3 * 3600 - 130, 3 * 3600);
        Assert.NotNull(fiveHour["pace"]);
        Assert.NotNull(fiveHour["projection"]);
    }

    [Fact]
    public void Pace_verdict_matches_the_widget_math()
    {
        using var tmp = new TempDir();
        // five_hour at 42% with 3h left: frac = (5h-3h)/5h = 40% through, util 42
        // -> |42-40| < 5 -> on_pace.
        var r = Logic(tmp, OkSnapshotJson(Now), Now).BuildUsage();
        var pace = (JsonObject)((JsonArray)r["tiers"]!)[0]!["pace"]!;
        Assert.Equal("on_pace", (string?)pace["verdict"]);
    }

    [Fact]
    public void Routines_gets_null_pace_and_projection_but_keeps_counts()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, OkSnapshotJson(Now), Now).BuildUsage();
        var routines = (JsonObject)((JsonArray)r["tiers"]!)[2]!;
        Assert.Null(routines["pace"]?.AsObject());
        Assert.Null(routines["projection"]?.AsObject());
        Assert.Equal(3, (int)routines["used"]!.GetValue<int>());
        Assert.Equal(15, (int)routines["limit"]!.GetValue<int>());
    }

    [Fact]
    public void Stale_band_reports_stale_status_with_data_present()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-10)), Now).BuildUsage();
        Assert.Equal("stale", (string?)r["status"]);
        Assert.Null(r["reason"]?.GetValue<string>());
        Assert.Equal(3, ((JsonArray)r["tiers"]!).Count);
    }

    [Fact]
    public void Dead_band_names_the_widget_as_the_problem()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-43)), Now).BuildUsage();
        Assert.Equal("stale", (string?)r["status"]);
        Assert.Equal("widget_not_polling", (string?)r["reason"]);
        Assert.Contains("start", (string?)r["remedy"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Error_status_snapshot_surfaces_fetch_error_and_last_good_tiers()
    {
        using var tmp = new TempDir();
        string json = OkSnapshotJson(Now.AddMinutes(-2))
            .Replace("\"status\":\"ok\"", "\"status\":\"error\"")
            .Replace("\"error_kind\":null", "\"error_kind\":\"session_expired\"");
        var r = Logic(tmp, json, Now).BuildUsage();
        Assert.Equal("stale", (string?)r["status"]);           // last-good, not current
        Assert.Equal("session_expired", (string?)r["fetch_error"]);
        Assert.Contains("re-authenticate", (string?)r["remedy"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, ((JsonArray)r["tiers"]!).Count);
    }

    [Fact]
    public void Crossed_reset_suppresses_utilization_never_serves_the_stale_percent()
    {
        using var tmp = new TempDir();
        // five_hour reset instant 1 minute in the past, snapshot only 2m old:
        // fresh by age, arbitrarily wrong across the boundary.
        string json = $$"""
            {"schema_version":1,"captured_at":"{{Iso(Now.AddMinutes(-2))}}","status":"ok","error_kind":null,
             "account_ref":"x","plan":"Max 20x",
             "tiers":[{"key":"five_hour","utilization":88,"resets_at":"{{Iso(Now.AddMinutes(-1))}}","used":null,"limit":null}]}
            """;
        var r = Logic(tmp, json, Now).BuildUsage();
        var tier = (JsonObject)((JsonArray)r["tiers"]!)[0]!;
        Assert.True(tier["reset_crossed"]!.GetValue<bool>());
        Assert.Null(tier["utilization_pct"]?.GetValue<int>());
        Assert.Null(tier["headroom_pct"]?.GetValue<int>());
        Assert.Null(tier["pace"]?.AsObject());
        Assert.Equal(0, (double)tier["resets_in_seconds"]!.GetValue<double>());
    }

    [Fact]
    public void No_consented_roots_means_null_local_burn()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, OkSnapshotJson(Now), Now).BuildUsage();
        Assert.Null(r["local_burn_since_snapshot"]?.AsObject());
    }

    [Fact]
    public void Local_burn_since_snapshot_scopes_to_consented_roots_and_maps_tiers()
    {
        using var tmp = new TempDir();
        string rootA = Path.Combine(tmp.Path, ".claude-personal");
        string rootB = Path.Combine(tmp.Path, ".claude");   // NOT consented
        WriteSessionLog(rootA, "c--proj-a",
            EventLine(Now.AddMinutes(-1), "claude-fable-5", 1000, 2000, @"C:\proj\a"),
            EventLine(Now.AddMinutes(-1), "mystery-model-9", 50, 50, @"C:\proj\a"),
            EventLine(Now.AddMinutes(-30), "claude-fable-5", 999, 999, @"C:\proj\a")); // pre-snapshot: excluded
        WriteSessionLog(rootB, "c--proj-b",
            EventLine(Now.AddMinutes(-1), "claude-sonnet-5", 7777, 0, @"C:\proj\b"));

        var logic = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-2)), Now, (".claude-personal", rootA));
        var burn = (JsonObject)logic.BuildUsage()["local_burn_since_snapshot"]!;

        // 3000 fable + 100 unmapped; the unconsented root's 7777 never appears.
        Assert.Equal(3100, (long)burn["total_tokens"]!.GetValue<long>());
        Assert.Equal(3000, (long)burn["by_tier"]!["seven_day_fable"]!.GetValue<long>());
        Assert.Contains("proxy", (string?)burn["caveat"]);
    }
}
