using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class SeatRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private static JsonObject Payload(double util) => new()
    {
        ["five_hour"] = new JsonObject { ["utilization"] = util, ["resets_at"] = "2026-10-11T15:00:00+00:00" },
    };

    private static string Ref(string label) => SnapshotContract.AccountRef(label)!;

    // Which accounts

    [Fact]
    public void Fetchable_needs_a_link_a_key_and_not_the_active_account()
    {
        var linked = new Dictionary<string, string> { ["Work"] = @"C:\w", ["Home"] = @"C:\h" };
        var signedIn = new HashSet<string> { "Work", "Home", "Other" };
        Assert.True(SeatRules.Fetchable("Work", linked, signedIn, "Home"));
        Assert.False(SeatRules.Fetchable("Home", linked, signedIn, "Home"));
        Assert.False(SeatRules.Fetchable("Other", linked, signedIn, "Home"));
        Assert.False(SeatRules.Fetchable("Work", linked, new HashSet<string> { "Home" }, "Home"));
    }

    // Timing

    [Fact]
    public void Throttled_is_true_inside_nine_tenths_of_the_interval_and_false_after()
    {
        Assert.False(SeatRules.Throttled(null, Now, Interval));
        Assert.True(SeatRules.Throttled(Now - TimeSpan.FromMinutes(4), Now, Interval));
        Assert.False(SeatRules.Throttled(Now - TimeSpan.FromSeconds(271), Now, Interval)); // 4.5 min exactly is the edge
        Assert.False(SeatRules.Throttled(Now - TimeSpan.FromMinutes(6), Now, Interval));
    }

    [Fact]
    public void NeedsReading_when_the_file_is_missing_or_an_interval_old()
    {
        Assert.True(SeatRules.NeedsReading(null, Now, Interval));
        Assert.True(SeatRules.NeedsReading(Now - Interval, Now, Interval));
        Assert.False(SeatRules.NeedsReading(Now - TimeSpan.FromMinutes(4), Now, Interval));
    }

    [Fact]
    public void Stagger_is_five_seconds_and_the_activity_window_ten_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), SeatRules.Stagger);
        Assert.Equal(TimeSpan.FromMinutes(10), SeatRules.ActivityWindow);
    }

    // Folder activity: file times only

    private static string Log(string folder, string project, string name, DateTime writtenUtc)
    {
        var dir = Path.Combine(folder, "projects", project);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);
        File.WriteAllText(file, "not even json; it is never opened");
        File.SetLastWriteTimeUtc(file, writtenUtc);
        return file;
    }

    [Fact]
    public void RecentActivity_is_false_without_a_projects_folder_or_a_missing_folder()
    {
        using var tmp = new TempDir();
        Assert.False(SeatRules.RecentActivity(tmp.Path, DateTime.UtcNow));
        Assert.False(SeatRules.RecentActivity(Path.Combine(tmp.Path, "nope"), DateTime.UtcNow));
    }

    [Fact]
    public void RecentActivity_looks_at_the_newest_jsonl_write_time_in_any_project()
    {
        using var tmp = new TempDir();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        Log(tmp.Path, "a", "old.jsonl", now - TimeSpan.FromMinutes(30));
        Assert.False(SeatRules.RecentActivity(tmp.Path, now));
        Log(tmp.Path, "b", "new.jsonl", now - TimeSpan.FromMinutes(9));
        Assert.True(SeatRules.RecentActivity(tmp.Path, now));
    }

    [Fact]
    public void RecentActivity_ignores_other_files_and_honors_the_window_edge()
    {
        using var tmp = new TempDir();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        Log(tmp.Path, "a", "notes.txt", now);
        Assert.False(SeatRules.RecentActivity(tmp.Path, now));
        Log(tmp.Path, "a", "edge.jsonl", now - SeatRules.ActivityWindow);
        Assert.True(SeatRules.RecentActivity(tmp.Path, now));
        Assert.False(SeatRules.RecentActivity(tmp.Path, now + TimeSpan.FromSeconds(1)));
    }

    // Readings and errors

    [Fact]
    public void SessionUtilization_reads_the_five_hour_tier_as_a_number()
    {
        Assert.Equal(42.5, SeatRules.SessionUtilization(Payload(42.5)));
        Assert.Equal(7, SeatRules.SessionUtilization(new JsonObject { ["five_hour"] = new JsonObject { ["utilization"] = 7 } }));
        Assert.Null(SeatRules.SessionUtilization(null));
        Assert.Null(SeatRules.SessionUtilization(new JsonObject()));
        Assert.Null(SeatRules.SessionUtilization(new JsonObject { ["five_hour"] = new JsonObject { ["utilization"] = null } }));
        Assert.Null(SeatRules.SessionUtilization(new JsonObject { ["five_hour"] = new JsonObject { ["utilization"] = "x" } }));
    }

    [Fact]
    public void ErrorKind_maps_the_typed_errors_and_only_auth_kinds_skip()
    {
        Assert.Equal("session_expired", SeatRules.ErrorKind(new SessionExpiredException("x")));
        Assert.Equal("cloudflare", SeatRules.ErrorKind(new CloudflareBlockedException("x")));
        Assert.Equal("network", SeatRules.ErrorKind(new NetworkException("x")));
        Assert.Equal("network", SeatRules.ErrorKind(new InvalidOperationException()));
        Assert.True(SeatRules.IsAuthKind("session_expired"));
        Assert.True(SeatRules.IsAuthKind("cloudflare"));
        Assert.False(SeatRules.IsAuthKind("network"));
    }

    // The files the services keep in line

    [Fact]
    public void WriteAccountError_keeps_the_last_good_tiers()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(40), "Max", Now);
        SeatFiles.WriteAccountError(tmp.Path, "Work", "network", "Max", Now.AddMinutes(5));
        var doc = (JsonObject)JsonNode.Parse(File.ReadAllText(SeatFiles.SnapshotPath(tmp.Path, Ref("Work"))))!;
        Assert.Equal("error", (string?)doc["status"]);
        Assert.Equal("network", (string?)doc["error_kind"]);
        Assert.NotEmpty((JsonArray)doc["tiers"]!);
    }

    [Fact]
    public void CapturedAt_reads_the_stamp_and_is_null_when_missing()
    {
        using var tmp = new TempDir();
        Assert.Null(SeatFiles.CapturedAt(tmp.Path, "Work"));
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(40), null, Now);
        var at = SeatFiles.CapturedAt(tmp.Path, "Work");
        Assert.NotNull(at);
        Assert.Equal(Now, at!.Value.ToUniversalTime());
    }

    [Fact]
    public void Sync_writes_seats_prunes_strays_and_marks_signed_out_accounts()
    {
        using var tmp = new TempDir();
        var work = Path.Combine(tmp.Path, "w");
        var home = Path.Combine(tmp.Path, "h");
        var linked = new Dictionary<string, string> { ["Work"] = work, ["Home"] = home };
        SeatFiles.WriteAccount(tmp.Path, "Gone", Payload(1), null, Now);

        SeatFiles.Sync(tmp.Path, linked, new HashSet<string> { "Work" }, Now);

        Assert.False(File.Exists(SeatFiles.SnapshotPath(tmp.Path, Ref("Gone"))));
        Assert.True(SeatFiles.IsSignedOut(tmp.Path, "Home"));
        Assert.False(File.Exists(SeatFiles.SnapshotPath(tmp.Path, Ref("Work"))));
        var seats = (JsonArray)((JsonObject)JsonNode.Parse(File.ReadAllText(SeatFiles.SeatsPath(tmp.Path)))!)["seats"]!;
        Assert.Equal(2, seats.Count);

        // Home signs in again: its signed-out file goes until its next reading.
        SeatFiles.Sync(tmp.Path, linked, new HashSet<string> { "Work", "Home" }, Now);
        Assert.False(File.Exists(SeatFiles.SnapshotPath(tmp.Path, Ref("Home"))));

        // A real reading is left alone by later syncs.
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(10), null, Now);
        SeatFiles.Sync(tmp.Path, linked, new HashSet<string> { "Work", "Home" }, Now);
        Assert.True(File.Exists(SeatFiles.SnapshotPath(tmp.Path, Ref("Work"))));
    }

    [Fact]
    public void Sync_with_no_links_leaves_neither_file()
    {
        using var tmp = new TempDir();
        var linked = new Dictionary<string, string> { ["Work"] = Path.Combine(tmp.Path, "w") };
        SeatFiles.Sync(tmp.Path, linked, new HashSet<string> { "Work" }, Now);
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(10), null, Now);
        SeatFiles.Sync(tmp.Path, new Dictionary<string, string>(), new HashSet<string> { "Work" }, Now);
        Assert.False(File.Exists(SeatFiles.SeatsPath(tmp.Path)));
        Assert.False(File.Exists(SeatFiles.SnapshotPath(tmp.Path, Ref("Work"))));
    }

    [Fact]
    public void WebView2ProfileDir_is_beside_the_shared_profile_and_named_by_ref()
    {
        using var tmp = new TempDir();
        var p = new Paths(tmp.Path);
        Assert.Equal(Path.Combine(p.AppDataDir, "webview2-" + Ref("Work")), p.WebView2ProfileDir(Ref("Work")));
        Assert.NotEqual(p.WebView2FetchDir, p.WebView2ProfileDir(Ref("Work")));
    }
}
