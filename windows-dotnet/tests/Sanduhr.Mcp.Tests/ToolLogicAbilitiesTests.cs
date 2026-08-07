using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

public class ToolLogicAbilitiesTests
{
    private static ToolLogic Logic(TempDir tmp, string? snapshotJson, params (string Name, string Path)[] roots)
    {
        string path = Path.Combine(tmp.Path, "snapshot.json");
        if (snapshotJson is not null)
            File.WriteAllText(path, snapshotJson);
        return new ToolLogic(Config(path, roots), () => Now);
    }

    // -- pacing depth on get_usage --------------------------------------------

    [Fact]
    public void Ahead_of_pace_carries_cooldown_and_projected_final()
    {
        using var tmp = new TempDir();
        // five_hour at 80% with 4h of 5h remaining -> frac 0.2, wildly ahead.
        string json = $$"""
            {"schema_version":1,"captured_at":"{{Iso(Now.AddMinutes(-1))}}","status":"ok","error_kind":null,
             "account_ref":"x","plan":"Max 20x",
             "tiers":[{"key":"five_hour","utilization":80,"resets_at":"{{Iso(Now.AddHours(4))}}","used":null,"limit":null}]}
            """;
        var pace = (JsonObject)((JsonArray)Logic(tmp, json).BuildUsage()["tiers"]!)[0]!["pace"]!;

        Assert.Equal("ahead", (string?)pace["verdict"]);
        // waitFrac = 0.8 - 0.2 = 0.6 of the 5h period = 10800s of idling to re-pace.
        Assert.Equal(10800, (double)pace["cooldown_seconds"]!.GetValue<double>());
        Assert.Null(pace["surplus_pct"]?.GetValue<double>());
        // 80 / 0.2 = 400, capped at the widget's own 200.
        Assert.Equal(200, (double)pace["projected_final_pct"]!.GetValue<double>());
    }

    [Fact]
    public void Under_pace_carries_surplus_and_no_cooldown()
    {
        using var tmp = new TempDir();
        // five_hour at 10% with 2.5h remaining -> frac 0.5, banked 40%.
        string json = $$"""
            {"schema_version":1,"captured_at":"{{Iso(Now.AddMinutes(-1))}}","status":"ok","error_kind":null,
             "account_ref":"x","plan":"Max 20x",
             "tiers":[{"key":"five_hour","utilization":10,"resets_at":"{{Iso(Now.AddHours(2.5))}}","used":null,"limit":null}]}
            """;
        var pace = (JsonObject)((JsonArray)Logic(tmp, json).BuildUsage()["tiers"]!)[0]!["pace"]!;

        Assert.Equal("under", (string?)pace["verdict"]);
        Assert.Equal(40, (double)pace["surplus_pct"]!.GetValue<double>());
        Assert.Null(pace["cooldown_seconds"]?.GetValue<double>());
        // 10 / 0.5 -> lands at 20% if momentum holds.
        Assert.Equal(20, (double)pace["projected_final_pct"]!.GetValue<double>());
    }

    // -- get_model_usage ------------------------------------------------------

    [Fact]
    public void Model_usage_guards_window_and_consent()
    {
        using var tmp = new TempDir();
        Assert.Equal("invalid_params", (string?)Logic(tmp, null).BuildModelUsage(3)["reason"]);
        Assert.Equal("disabled", (string?)Logic(tmp, null).BuildModelUsage(7)["reason"]);
    }

    [Fact]
    public void Model_usage_ranks_models_and_joins_the_weekly_meter()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(personal, "c--x",
            EventLine(Now.AddHours(-2), "claude-fable-5", 6000, 2000, @"C:\p\x"),
            EventLine(Now.AddHours(-1), "claude-sonnet-5", 1000, 500, @"C:\p\x"),
            EventLine(Now.AddHours(-1), "mystery-model-9", 400, 100, @"C:\p\x"));
        // Snapshot carries the fable weekly meter at 8%.
        string snap = $$"""
            {"schema_version":1,"captured_at":"{{Iso(Now.AddMinutes(-2))}}","status":"ok","error_kind":null,
             "account_ref":"x","plan":"Max 20x",
             "tiers":[{"key":"seven_day_fable","utilization":8,"resets_at":"{{Iso(Now.AddDays(5))}}","used":null,"limit":null}]}
            """;
        var r = Logic(tmp, snap, (".claude-personal", personal)).BuildModelUsage(7);

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(10000, (long)r["total_tokens"]!.GetValue<long>());
        var models = (JsonArray)r["models"]!;
        var top = (JsonObject)models[0]!;
        Assert.Equal("claude-fable-5", (string?)top["model"]);
        Assert.Equal(8000, (long)top["tokens"]!.GetValue<long>());
        Assert.Equal(80, (double)top["share_pct"]!.GetValue<double>());
        Assert.Equal("seven_day_fable", (string?)top["tier_key"]);
        Assert.Equal("Weekly - Fable", (string?)top["tier_label"]);
        Assert.Equal(8, (int)top["meter_utilization_pct"]!.GetValue<int>());
        // Unmapped model stays visible with null tier — never silently dropped.
        var mystery = (JsonObject)models.Single(m => (string?)m!["model"] == "mystery-model-9")!;
        Assert.Null(mystery["tier_key"]?.GetValue<string>());
        Assert.Null(mystery["meter_utilization_pct"]?.GetValue<int>());
        Assert.NotNull(r["meter_source"]);
    }

    [Fact]
    public void Model_usage_without_a_snapshot_still_serves_tokens()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(personal, "c--x",
            EventLine(Now.AddHours(-1), "claude-fable-5", 100, 0, @"C:\p\x"));
        var r = Logic(tmp, null, (".claude-personal", personal)).BuildModelUsage(7);
        Assert.Equal("ok", (string?)r["status"]);
        Assert.Null(r["meter_source"]?.AsObject());
        Assert.Null(((JsonObject)((JsonArray)r["models"]!)[0]!)["meter_utilization_pct"]?.GetValue<int>());
    }

    // -- get_usage_history ----------------------------------------------------

    private static void WriteVaultRollup(McpConfig config, string rootName, string month,
        Dictionary<string, VaultRollupDay> days)
    {
        var store = new VaultStore(config.VaultDir);
        store.SaveRollupShard(rootName, month, new VaultRollupShard { Days = days });
    }

    [Fact]
    public void History_guards_window_and_consent()
    {
        using var tmp = new TempDir();
        Assert.Equal("invalid_params", (string?)Logic(tmp, null).BuildHistory(14)["reason"]);
        Assert.Equal("disabled", (string?)Logic(tmp, null).BuildHistory(30)["reason"]);
    }

    [Fact]
    public void History_with_no_vault_is_missing_with_a_vault_remedy()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        Directory.CreateDirectory(personal);
        var r = Logic(tmp, null, (".claude-personal", personal)).BuildHistory(30);
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("missing", (string?)r["reason"]);
        Assert.Contains("vault", (string?)r["remedy"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void History_serves_recorded_days_with_split_and_omits_absent_days()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        Directory.CreateDirectory(personal);
        var logic = Logic(tmp, null, (".claude-personal", personal));

        // Now = 2026-07-26 12:00Z; local "today" 2026-07-26 (offset-safe in tests: UTC clock).
        WriteVaultRollup(Config(Path.Combine(tmp.Path, "snapshot.json"), (".claude-personal", personal)),
            ".claude-personal", "2026-07",
            new Dictionary<string, VaultRollupDay>
            {
                ["2026-07-24"] = new()
                {
                    Total = 500_000, Input = 100_000, Output = 400_000,
                    ByProject = { ["Sanduhr~abc123"] = 300_000, ["vibe-plugins~def456"] = 200_000 },
                },
                ["2026-07-25"] = new() { Total = 250_000, Input = 50_000, Output = 200_000 },
                // 2026-07-23 deliberately absent: a no-record day, never a zero.
            });

        var r = logic.BuildHistory(7);

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(750_000, (long)r["total_tokens"]!.GetValue<long>());
        Assert.Equal(2, (int)r["days_recorded"]!.GetValue<int>());
        var days = (JsonArray)r["days"]!;
        Assert.Equal("2026-07-24", (string?)days[0]!["date"]);
        Assert.Equal(100_000, (long)days[0]!["sent"]!.GetValue<long>());
        Assert.Equal(400_000, (long)days[0]!["received"]!.GetValue<long>());
        Assert.DoesNotContain("2026-07-23", r.ToJsonString());
        // Project keys are name~hash — the response carries display names only.
        var top = (JsonObject)((JsonArray)r["top_projects"]!)[0]!;
        Assert.Equal("Sanduhr", (string?)top["name"]);
        Assert.Equal(300_000, (long)top["tokens"]!.GetValue<long>());
        Assert.Contains("no-record", (string?)r["caveat"]);
    }

    [Fact]
    public void History_window_excludes_days_before_the_range()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        Directory.CreateDirectory(personal);
        var logic = Logic(tmp, null, (".claude-personal", personal));
        WriteVaultRollup(Config(Path.Combine(tmp.Path, "snapshot.json"), (".claude-personal", personal)),
            ".claude-personal", "2026-07",
            new Dictionary<string, VaultRollupDay>
            {
                ["2026-07-10"] = new() { Total = 999 },   // outside a 7-day window ending 07-26
                ["2026-07-25"] = new() { Total = 111 },
            });

        var r = logic.BuildHistory(7);
        Assert.Equal(111, (long)r["total_tokens"]!.GetValue<long>());
        Assert.Equal(1, (int)r["days_recorded"]!.GetValue<int>());
    }
}
