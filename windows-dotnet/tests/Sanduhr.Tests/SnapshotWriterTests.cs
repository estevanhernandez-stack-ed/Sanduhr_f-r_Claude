using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class SnapshotWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static JsonObject SamplePayload() => new()
    {
        ["five_hour"] = new JsonObject { ["utilization"] = 42.0, ["resets_at"] = "2026-07-26T15:00:00+00:00" },
        ["seven_day"] = new JsonObject { ["utilization"] = 62.0, ["resets_at"] = "2026-07-31T00:00:00+00:00" },
        ["routines"] = new JsonObject { ["utilization"] = 20.0, ["resets_at"] = null, ["used"] = 3, ["limit"] = 15 },
        ["seven_day_opus"] = new JsonObject { ["utilization"] = null }, // null util, no counts → skipped
    };

    private static JsonObject ReadSnapshot(string path)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    [Fact]
    public void WriteOk_produces_a_schema_v1_snapshot_with_tiers_from_the_payload()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        new SnapshotWriter(path).WriteOk(SamplePayload(), "Max 20x", "Este", Now);

        var snap = ReadSnapshot(path);
        Assert.Equal(SnapshotContract.SchemaVersion, (int)snap["schema_version"]!.GetValue<int>());
        Assert.Equal("ok", (string?)snap["status"]);
        Assert.Null(snap["error_kind"]?.GetValue<string>());
        Assert.Equal("Max 20x", (string?)snap["plan"]);
        Assert.Equal(SnapshotContract.AccountRef("Este"), (string?)snap["account_ref"]);
        Assert.Equal(Now, DateTimeOffset.Parse((string)snap["captured_at"]!.GetValue<string>()!));

        var tiers = (JsonArray)snap["tiers"]!;
        var keys = tiers.Select(t => (string?)t!["key"]).ToList();
        Assert.Equal(new[] { "five_hour", "seven_day", "routines" }, keys);

        var fiveHour = (JsonObject)tiers[0]!;
        Assert.Equal(42, (int)fiveHour["utilization"]!.GetValue<int>());
        Assert.Equal("2026-07-26T15:00:00+00:00", (string?)fiveHour["resets_at"]);

        var routines = (JsonObject)tiers[2]!;
        Assert.Equal(3, (int)routines["used"]!.GetValue<int>());
        Assert.Equal(15, (int)routines["limit"]!.GetValue<int>());
    }

    [Fact]
    public void WriteOk_never_writes_the_raw_account_label()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        new SnapshotWriter(path).WriteOk(SamplePayload(), "Max 20x", "SuperSecretLabel", Now);
        Assert.DoesNotContain("SuperSecretLabel", File.ReadAllText(path));
    }

    [Fact]
    public void WriteOk_leaves_no_tmp_file_behind()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        new SnapshotWriter(path).WriteOk(SamplePayload(), null, null, Now);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteOk_replaces_a_corrupt_existing_file()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        File.WriteAllText(path, "{ not json");
        new SnapshotWriter(path).WriteOk(SamplePayload(), null, null, Now);
        Assert.Equal("ok", (string?)ReadSnapshot(path)["status"]);
    }

    [Fact]
    public void WriteError_keeps_the_last_good_tiers_and_stamps_the_kind()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        var writer = new SnapshotWriter(path);
        writer.WriteOk(SamplePayload(), "Max 20x", "Este", Now);
        writer.WriteError(SnapshotContract.ErrorSessionExpired, "Max 20x", "Este", Now.AddMinutes(5));

        var snap = ReadSnapshot(path);
        Assert.Equal("error", (string?)snap["status"]);
        Assert.Equal(SnapshotContract.ErrorSessionExpired, (string?)snap["error_kind"]);
        // last-good tiers carried so "stale" stays actionable
        Assert.Equal(3, ((JsonArray)snap["tiers"]!).Count);
        var captured = DateTimeOffset.Parse((string)snap["captured_at"]!.GetValue<string>()!);
        Assert.Equal(Now.AddMinutes(5), captured);
    }

    [Fact]
    public void WriteError_with_no_prior_file_writes_empty_tiers()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        new SnapshotWriter(path).WriteError(SnapshotContract.ErrorNetwork, null, null, Now);

        var snap = ReadSnapshot(path);
        Assert.Equal("error", (string?)snap["status"]);
        Assert.Empty((JsonArray)snap["tiers"]!);
    }

    [Fact]
    public void WriteError_over_a_corrupt_file_does_not_throw()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        File.WriteAllText(path, "\0\0garbage");
        new SnapshotWriter(path).WriteError(SnapshotContract.ErrorCloudflare, null, null, Now);
        Assert.Equal("error", (string?)ReadSnapshot(path)["status"]);
    }

    [Fact]
    public void Delete_removes_the_file_and_tolerates_absence()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        var writer = new SnapshotWriter(path);
        writer.WriteOk(SamplePayload(), null, null, Now);
        writer.Delete();
        Assert.False(File.Exists(path));
        writer.Delete(); // second delete is a no-op, not a throw
    }
}
