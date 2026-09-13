using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>
/// Issue #63: a widget older than 3.4.0 polls and appends history but never
/// writes snapshot.json. The stale/no-data classification must say so
/// (<c>widget_too_old</c>, not <c>widget_not_polling</c>) and serve the history
/// point as <c>degraded</c> instead of nothing.
/// </summary>
public class ToolLogicHistoryFallbackTests
{
    private static ToolLogic Logic(TempDir tmp, string? snapshotJson, DateTimeOffset now)
    {
        string path = Path.Combine(tmp.Path, "snapshot.json");
        if (snapshotJson is not null)
            File.WriteAllText(path, snapshotJson);
        return new ToolLogic(Config(path), () => now);
    }

    /// <summary>A snapshot 48 days old written by a dev build — the exact file
    /// from the issue.</summary>
    private static string DeadDevSnapshot()
        => OkSnapshotJson(Now.AddDays(-48)).Replace("\"writer_version\":\"3.4.0\"", "\"writer_version\":\"1.0.0\"");

    private static void FreshHistory(TempDir tmp, string label = "Este")
        => WriteHistory(tmp.Path, label,
            ("five_hour", Now.AddMinutes(-3), 69, Now.AddMinutes(39)),
            ("seven_day", Now.AddMinutes(-3), 14, Now.AddDays(6)),
            ("extra_usage", Now.AddMinutes(-3), 0, null),
            ("seven_day_sonnet", Now.AddDays(-80), 1, Now.AddDays(-77)));   // dormant tier: not current

    // -- missing snapshot ----------------------------------------------------

    [Fact]
    public void Missing_snapshot_with_fresh_history_is_degraded_widget_too_old()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp);
        var r = Logic(tmp, null, Now).BuildUsage();

        Assert.Equal("degraded", (string?)r["status"]);
        Assert.Equal("widget_too_old", (string?)r["reason"]);
        Assert.Contains("3.4.0 or later", (string?)r["remedy"]);
        Assert.Contains("update it", (string?)r["remedy"]);
        Assert.Equal("history", (string?)r["data_source"]);
        Assert.Equal(180, r["age_seconds"]!.GetValue<double>());
        Assert.Null(r["writer_version"]?.GetValue<string>());
        Assert.Null(r["snapshot_age_seconds"]?.GetValue<double>());
        Assert.Contains("history file", (string?)r["data_lag_note"]);
        Assert.Contains(ToolLogic.DataLagNote, (string?)r["data_lag_note"]);
    }

    [Fact]
    public void Missing_snapshot_with_stale_history_stays_no_data_missing()
    {
        using var tmp = new TempDir();
        WriteHistory(tmp.Path, "Este", ("five_hour", Now.AddMinutes(-16), 50, Now.AddHours(1)));
        var r = Logic(tmp, null, Now).BuildUsage();
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("missing", (string?)r["reason"]);
    }

    // -- dead snapshot -------------------------------------------------------

    [Fact]
    public void Dead_dev_snapshot_with_fresh_history_is_degraded_and_names_the_leftover()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp);
        var r = Logic(tmp, DeadDevSnapshot(), Now).BuildUsage();

        Assert.Equal("degraded", (string?)r["status"]);
        Assert.Equal("widget_too_old", (string?)r["reason"]);
        Assert.Contains("dev-build or pre-3.4.0 leftover", (string?)r["remedy"]);
        Assert.Contains("build 1.0.0", (string?)r["remedy"]);
        Assert.Equal("1.0.0", (string?)r["writer_version"]);
        Assert.Equal(48 * 86400, r["snapshot_age_seconds"]!.GetValue<double>());
        // The served numbers are the history's, not the 48-day-old snapshot's.
        Assert.Equal(180, r["age_seconds"]!.GetValue<double>());
        Assert.Equal(Iso(Now.AddMinutes(-3)), (string?)r["as_of"]);
    }

    [Fact]
    public void Dead_dev_snapshot_without_fresh_history_is_stale_widget_too_old_not_restart()
    {
        using var tmp = new TempDir();
        var r = Logic(tmp, DeadDevSnapshot(), Now).BuildUsage();

        Assert.Equal("stale", (string?)r["status"]);
        Assert.Equal("widget_too_old", (string?)r["reason"]);
        Assert.Contains("leftover", (string?)r["remedy"]);
        Assert.Contains("3.4.0 or later", (string?)r["remedy"]);
        Assert.Contains("may also not be running", (string?)r["remedy"]);
        Assert.DoesNotContain("restart", (string?)r["remedy"]);
        Assert.Equal("snapshot", (string?)r["data_source"]);
        Assert.Equal(3, ((JsonArray)r["tiers"]!).Count);   // last-known, still visible
    }

    [Fact]
    public void Dead_snapshot_without_writer_version_counts_as_below_the_floor()
    {
        using var tmp = new TempDir();
        string json = OkSnapshotJson(Now.AddMinutes(-43)).Replace("\"writer_version\":\"3.4.0\",", "");
        var r = Logic(tmp, json, Now).BuildUsage();
        Assert.Equal("stale", (string?)r["status"]);
        Assert.Equal("widget_too_old", (string?)r["reason"]);
        Assert.Contains("build unknown", (string?)r["remedy"]);
    }

    [Fact]
    public void Dead_release_snapshot_with_stale_history_is_widget_not_polling()
    {
        using var tmp = new TempDir();
        WriteHistory(tmp.Path, "Este", ("five_hour", Now.AddMinutes(-40), 50, Now.AddHours(1)));
        var r = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-43)), Now).BuildUsage();
        Assert.Equal("stale", (string?)r["status"]);
        Assert.Equal("widget_not_polling", (string?)r["reason"]);
        Assert.Contains("start (or restart)", (string?)r["remedy"]);
        Assert.Contains("build 3.4.0", (string?)r["remedy"]);
    }

    [Fact]
    public void Dead_release_snapshot_with_fresh_history_is_degraded_and_points_at_the_integration_toggle()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp);
        var r = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-43)), Now).BuildUsage();
        Assert.Equal("degraded", (string?)r["status"]);
        Assert.Equal("widget_too_old", (string?)r["reason"]);
        Assert.Contains("re-enable", (string?)r["remedy"]);
        Assert.DoesNotContain("leftover", (string?)r["remedy"]);
    }

    [Fact]
    public void Fresh_dev_snapshot_is_ok_a_running_dev_build_is_current()
    {
        using var tmp = new TempDir();
        string json = OkSnapshotJson(Now.AddMinutes(-2)).Replace("\"writer_version\":\"3.4.0\"", "\"writer_version\":\"1.0.0\"");
        var r = Logic(tmp, json, Now).BuildUsage();
        Assert.Equal("ok", (string?)r["status"]);
        Assert.Null(r["reason"]?.GetValue<string>());
    }

    // -- degraded payload shape ---------------------------------------------

    [Fact]
    public void Degraded_tiers_carry_what_history_records_and_null_what_it_does_not()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp);
        var r = Logic(tmp, null, Now).BuildUsage();
        var tiers = (JsonArray)r["tiers"]!;

        // Canonical order, and the dormant sonnet tier (last point 80 days ago) is not served.
        Assert.Equal(new[] { "five_hour", "seven_day", "extra_usage" }, tiers.Select(t => (string?)t!["key"]).ToArray());

        var fiveHour = (JsonObject)tiers[0]!;
        Assert.Equal("Session (5hr)", (string?)fiveHour["label"]);
        Assert.Equal(69, fiveHour["utilization_pct"]!.GetValue<int>());
        Assert.Equal(31, fiveHour["headroom_pct"]!.GetValue<int>());
        Assert.Equal(Iso(Now.AddMinutes(39)), (string?)fiveHour["resets_at"]);
        Assert.Equal(39 * 60, fiveHour["resets_in_seconds"]!.GetValue<double>());
        Assert.False(fiveHour["reset_crossed"]!.GetValue<bool>());
        Assert.Equal(Iso(Now.AddMinutes(-3)), (string?)fiveHour["as_of"]);
        Assert.NotNull(fiveHour["pace"]);
        Assert.NotNull(fiveHour["projection"]);
        // Not in the history schema: null, never fabricated.
        Assert.Null(fiveHour["used"]?.GetValue<int>());
        Assert.Null(fiveHour["limit"]?.GetValue<int>());
        Assert.Null(r["account"]!["plan"]?.GetValue<string>());

        // extra_usage has no reset instant in history: unknown stays distinct from none.
        var extra = (JsonObject)tiers[2]!;
        Assert.Null(extra["resets_at"]?.GetValue<string>());
        Assert.Null(extra["resets_in_seconds"]?.GetValue<double>());
        Assert.Null(extra["pace"]?.AsObject());
    }

    [Fact]
    public void Degraded_applies_reset_crossing_like_the_snapshot_path()
    {
        using var tmp = new TempDir();
        WriteHistory(tmp.Path, "Este", ("five_hour", Now.AddMinutes(-4), 88, Now.AddMinutes(-1)));
        var r = Logic(tmp, null, Now).BuildUsage();
        var tier = (JsonObject)((JsonArray)r["tiers"]!)[0]!;
        Assert.True(tier["reset_crossed"]!.GetValue<bool>());
        Assert.Null(tier["utilization_pct"]?.GetValue<int>());
    }

    [Fact]
    public void Degraded_account_ref_is_the_hashed_label_and_the_label_never_leaks()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp, "MySecretWorkAccount");
        var r = Logic(tmp, null, Now).BuildUsage();
        Assert.Equal(SnapshotContract.AccountRef("MySecretWorkAccount"), (string?)r["account"]!["ref"]);
        Assert.DoesNotContain("MySecretWorkAccount", r.ToJsonString());
    }

    [Fact]
    public void Degraded_local_burn_anchors_on_the_history_point()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp);
        string root = Path.Combine(tmp.Path, ".claude-personal");
        WriteSessionLog(root, "c--proj",
            EventLine(Now.AddMinutes(-1), "claude-fable-5", 1000, 2000, @"C:\proj"),
            EventLine(Now.AddMinutes(-10), "claude-fable-5", 999, 999, @"C:\proj"));   // before the point: excluded
        var logic = new ToolLogic(Config(Path.Combine(tmp.Path, "snapshot.json"), (".claude-personal", root)), () => Now);
        var burn = (JsonObject)logic.BuildUsage()["local_burn_since_snapshot"]!;
        Assert.Equal(3000, burn["total_tokens"]!.GetValue<long>());
    }

    // -- which history file ---------------------------------------------------

    [Fact]
    public void Prefers_the_history_matching_the_snapshot_account_ref_when_it_is_fresh()
    {
        using var tmp = new TempDir();
        // Snapshot account_ref d6ab2208 == AccountRef("Este") (the fixture's).
        Assert.Equal("d6ab2208", SnapshotContract.AccountRef("Este"));
        WriteHistory(tmp.Path, "Este", ("five_hour", Now.AddMinutes(-9), 11, Now.AddHours(1)));
        WriteHistory(tmp.Path, "Work", ("five_hour", Now.AddMinutes(-2), 77, Now.AddHours(1)));   // fresher, other account
        var r = Logic(tmp, DeadDevSnapshot(), Now).BuildUsage();
        Assert.Equal("degraded", (string?)r["status"]);
        Assert.Equal("d6ab2208", (string?)r["account"]!["ref"]);
        Assert.Equal(11, ((JsonArray)r["tiers"]!)[0]!["utilization_pct"]!.GetValue<int>());
    }

    [Fact]
    public void Falls_back_to_the_freshest_history_when_the_snapshot_account_is_stale_or_absent()
    {
        using var tmp = new TempDir();
        WriteHistory(tmp.Path, "Este", ("five_hour", Now.AddMinutes(-60), 11, Now.AddHours(1)));   // matches ref, stale
        WriteHistory(tmp.Path, "Work", ("five_hour", Now.AddMinutes(-2), 77, Now.AddHours(1)));
        var r = Logic(tmp, DeadDevSnapshot(), Now).BuildUsage();
        Assert.Equal("degraded", (string?)r["status"]);
        Assert.Equal(SnapshotContract.AccountRef("Work"), (string?)r["account"]!["ref"]);
        Assert.Equal(77, ((JsonArray)r["tiers"]!)[0]!["utilization_pct"]!.GetValue<int>());
    }

    [Fact]
    public void Malformed_and_empty_history_files_are_skipped_not_fatal()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "history.Broken.json"), "{ nope");
        File.WriteAllText(Path.Combine(tmp.Path, "history.Empty.json"), "{}");
        File.WriteAllText(Path.Combine(tmp.Path, "history.Torn.json"),
            """{"five_hour":[{"t":"not a date","v":5},{"t":null}]}""");
        var r = Logic(tmp, null, Now).BuildUsage();
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("missing", (string?)r["reason"]);
    }

    [Fact]
    public void Label_parsing_handles_spaces_and_the_legacy_unlabeled_file()
    {
        Assert.Equal("Account 2", HistoryReader.LabelFromFileName(@"C:\x\history.Account 2.json"));
        Assert.Equal("Este", HistoryReader.LabelFromFileName("history.Este.json"));
        Assert.Null(HistoryReader.LabelFromFileName("history.json"));
    }

    // -- floor ---------------------------------------------------------------

    [Theory]
    [InlineData("3.4.0", true)]
    [InlineData("3.4.0.0", true)]
    [InlineData("3.5.1", true)]
    [InlineData("3.3.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Writer_floor_is_3_4_0(string? writer, bool ok)
        => Assert.Equal(ok, ToolLogic.WriterMeetsFloor(writer));

    // -- ping ----------------------------------------------------------------

    [Fact]
    public void Ping_reports_the_floor_the_history_and_the_same_diagnosis()
    {
        using var tmp = new TempDir();
        FreshHistory(tmp);
        var p = Logic(tmp, DeadDevSnapshot(), Now).BuildPing();
        Assert.Equal("3.4.0", (string?)p["required_widget_version"]);
        Assert.Equal("1.0.0", (string?)p["snapshot_writer_version"]);
        Assert.False(p["snapshot_writer_meets_floor"]!.GetValue<bool>());
        Assert.True(p["history_found"]!.GetValue<bool>());
        Assert.True(p["history_fresh"]!.GetValue<bool>());
        Assert.Equal(180, p["history_age_seconds"]!.GetValue<double>());
        Assert.Equal("degraded", (string?)p["usage_status"]);
        Assert.Equal("widget_too_old", (string?)p["usage_reason"]);
        Assert.Contains("3.4.0 or later", (string?)p["remedy"]);
    }

    [Fact]
    public void Ping_without_snapshot_or_history_is_no_data_missing_and_null_floor_check()
    {
        using var tmp = new TempDir();
        var p = Logic(tmp, null, Now).BuildPing();
        Assert.False(p["snapshot_found"]!.GetValue<bool>());
        Assert.Null(p["snapshot_writer_meets_floor"]?.GetValue<bool>());
        Assert.False(p["history_found"]!.GetValue<bool>());
        Assert.Equal("no_data", (string?)p["usage_status"]);
        Assert.Equal("missing", (string?)p["usage_reason"]);
    }

    [Fact]
    public void Ping_on_a_fresh_release_snapshot_is_ok()
    {
        using var tmp = new TempDir();
        var p = Logic(tmp, OkSnapshotJson(Now.AddMinutes(-2)), Now).BuildPing();
        Assert.True(p["snapshot_writer_meets_floor"]!.GetValue<bool>());
        Assert.Equal("ok", (string?)p["usage_status"]);
        Assert.Null(p["usage_reason"]?.GetValue<string>());
    }
}
