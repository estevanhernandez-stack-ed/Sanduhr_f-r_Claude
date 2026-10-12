using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class SeatFilesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static JsonObject Payload() => new()
    {
        ["five_hour"] = new JsonObject { ["utilization"] = 42.0, ["resets_at"] = "2026-10-11T15:00:00+00:00" },
        ["seven_day"] = new JsonObject { ["utilization"] = 62.0, ["resets_at"] = "2026-10-16T00:00:00+00:00" },
    };

    private static JsonObject Read(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    private static string Ref(string label) => SnapshotContract.AccountRef(label)!;

    private static Dictionary<string, string> Links(string root) => new()
    {
        ["Work"] = Path.Combine(root, "zeta") + Path.DirectorySeparatorChar,
        ["Personal"] = Path.Combine(root, "Alpha"),
    };

    [Fact]
    public void Seats_are_sorted_by_folder_ignoring_case_and_trimmed()
    {
        using var tmp = new TempDir();
        var seats = SeatFiles.Seats(Links(tmp.Path));
        Assert.Equal(2, seats.Count);
        Assert.Equal(Path.Combine(tmp.Path, "Alpha"), seats[0].Folder);
        Assert.Equal(Ref("Personal"), seats[0].AccountRef);
        Assert.Equal(Path.Combine(tmp.Path, "zeta"), seats[1].Folder);
        Assert.Equal(Ref("Work"), seats[1].AccountRef);
    }

    [Fact]
    public void Linked_keeps_only_listed_accounts_with_a_folder()
    {
        var folders = new Dictionary<string, string> { ["Work"] = @"C:\w", ["Gone"] = @"C:\g", ["Blank"] = "" };
        var linked = SeatFiles.Linked(folders, new[] { "Work", "Blank", "Personal" });
        Assert.Equal(new[] { "Work" }, linked.Keys);
    }

    [Fact]
    public void No_label_appears_in_either_file()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteSeats(tmp.Path, SeatFiles.Seats(Links(tmp.Path)));
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), "Max 20x", Now);
        SeatFiles.WriteSignedOut(tmp.Path, "Personal", Now);
        var all = File.ReadAllText(SeatFiles.SeatsPath(tmp.Path))
            + File.ReadAllText(SeatFiles.SnapshotPath(tmp.Path, Ref("Work")))
            + File.ReadAllText(SeatFiles.SnapshotPath(tmp.Path, Ref("Personal")));
        Assert.DoesNotContain("Work", all);
        Assert.DoesNotContain("Personal", all);
        var doc = Read(SeatFiles.SeatsPath(tmp.Path));
        Assert.Equal(1, (int)doc["schema_version"]!);
        Assert.Equal(2, ((JsonArray)doc["seats"]!).Count);
    }

    [Fact]
    public void Empty_links_delete_seats_json_and_prune_every_seat_file()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteSeats(tmp.Path, SeatFiles.Seats(Links(tmp.Path)));
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), null, Now);
        SeatFiles.WriteSignedOut(tmp.Path, "Personal", Now);
        File.WriteAllText(Path.Combine(tmp.Path, "snapshot.json"), "{}");

        SeatFiles.WriteSeats(tmp.Path, SeatFiles.Seats(new Dictionary<string, string>()));
        SeatFiles.Prune(tmp.Path, Array.Empty<string>());

        Assert.False(File.Exists(SeatFiles.SeatsPath(tmp.Path)));
        Assert.Empty(SeatFiles.SnapshotRefs(tmp.Path));
        Assert.True(File.Exists(Path.Combine(tmp.Path, "snapshot.json")));
    }

    [Fact]
    public void Prune_keeps_linked_refs_only()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), null, Now);
        SeatFiles.WriteAccount(tmp.Path, "Personal", Payload(), null, Now);
        SeatFiles.Prune(tmp.Path, new[] { Ref("Work") });
        Assert.Equal(new HashSet<string> { Ref("Work") }, SeatFiles.SnapshotRefs(tmp.Path));
    }

    [Fact]
    public void Delete_removes_one_account_file()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), null, Now);
        SeatFiles.Delete(tmp.Path, "Work");
        Assert.Empty(SeatFiles.SnapshotRefs(tmp.Path));
        SeatFiles.Delete(tmp.Path, "Work"); // already gone: no throw
    }

    [Fact]
    public void Rename_moves_the_file_and_rewrites_the_ref()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), "Max 20x", Now);
        SeatFiles.Rename(tmp.Path, "Work", "Office");

        Assert.False(File.Exists(SeatFiles.SnapshotPath(tmp.Path, Ref("Work"))));
        var moved = Read(SeatFiles.SnapshotPath(tmp.Path, Ref("Office")));
        Assert.Equal(Ref("Office"), (string?)moved["account_ref"]);
        Assert.Equal("ok", (string?)moved["status"]);
        Assert.Equal(2, ((JsonArray)moved["tiers"]!).Count);
        Assert.DoesNotContain("Office", File.ReadAllText(SeatFiles.SnapshotPath(tmp.Path, Ref("Office"))));
    }

    [Fact]
    public void Ref_in_file_name_accepts_only_eight_lowercase_hex()
    {
        Assert.Equal("abcdef12", SeatFiles.RefInFileName("snapshot.abcdef12.json"));
        Assert.Null(SeatFiles.RefInFileName("snapshot.json"));
        Assert.Null(SeatFiles.RefInFileName("snapshot.ABCDEF12.json"));
        Assert.Null(SeatFiles.RefInFileName("snapshot.123.json"));
        Assert.Null(SeatFiles.RefInFileName("snapshot.abcdef12.json.tmp"));
        Assert.Null(SeatFiles.RefInFileName("snapshot.abcdefg1.json"));
        Assert.Null(SeatFiles.RefInFileName("seats.json"));
    }

    [Fact]
    public void The_account_file_has_the_same_schema_and_fields_as_snapshot_json()
    {
        using var tmp = new TempDir();
        var main = Path.Combine(tmp.Path, "snapshot.json");
        new SnapshotWriter(main, null, "9.9.9").WriteOk(Payload(), "Max 20x", "Work", Now);
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), "Max 20x", Now, "9.9.9");

        var a = Read(main);
        var b = Read(SeatFiles.SnapshotPath(tmp.Path, Ref("Work")));
        Assert.Equal(a.ToJsonString(), b.ToJsonString());
        Assert.Equal(SnapshotContract.SchemaVersion, (int)b["schema_version"]!);
    }

    [Fact]
    public void Signed_out_file_is_an_auth_error_with_no_tiers_even_over_an_ok_file()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), null, Now);
        SeatFiles.WriteSignedOut(tmp.Path, "Work", Now);
        var doc = Read(SeatFiles.SnapshotPath(tmp.Path, Ref("Work")));
        Assert.Equal("error", (string?)doc["status"]);
        Assert.Equal(SnapshotContract.ErrorSessionExpired, (string?)doc["error_kind"]);
        Assert.Empty((JsonArray)doc["tiers"]!);
        Assert.Equal(1, (int)doc["schema_version"]!);
    }

    [Fact]
    public void Writes_leave_no_temp_files()
    {
        using var tmp = new TempDir();
        SeatFiles.WriteSeats(tmp.Path, SeatFiles.Seats(Links(tmp.Path)));
        SeatFiles.WriteAccount(tmp.Path, "Work", Payload(), null, Now);
        Assert.DoesNotContain(Directory.GetFiles(tmp.Path), f => f.EndsWith(".tmp"));
    }
}

public class StatuslineSeatLookupTests
{
    private const string Profile = @"C:\Users\este";

    private static string Seats(params (string folder, string reference)[] rows)
    {
        var items = string.Join(",", rows.Select(r =>
            $"{{\"folder\":{System.Text.Json.JsonSerializer.Serialize(r.folder)},\"account_ref\":\"{r.reference}\"}}"));
        return $"{{\"schema_version\":1,\"seats\":[{items}]}}";
    }

    private static string Name(string? text, string? configDir) => StatuslineSeatLookup.SeatSnapshotName(text, configDir, Profile);

    [Fact]
    public void A_listed_folder_reads_its_account_file()
        => Assert.Equal("snapshot.abcdef12.json", Name(Seats((@"D:\seat\work", "abcdef12")), @"D:\seat\work"));

    [Fact]
    public void Comparison_ignores_case_and_trailing_separators()
        => Assert.Equal("snapshot.abcdef12.json", Name(Seats((@"D:\Seat\Work", "abcdef12")), @"d:\seat\work\"));

    [Fact]
    public void Unset_or_empty_config_dir_means_user_profile_dot_claude()
    {
        var text = Seats((@"C:\Users\este\.claude", "abcdef12"));
        Assert.Equal("snapshot.abcdef12.json", Name(text, null));
        Assert.Equal("snapshot.abcdef12.json", Name(text, ""));
    }

    [Fact]
    public void A_tilde_row_expands_against_the_profile()
        => Assert.Equal("snapshot.abcdef12.json", Name(Seats(("~\\.claude", "abcdef12")), null));

    [Fact]
    public void An_unlisted_folder_reads_snapshot_json()
        => Assert.Equal("snapshot.json", Name(Seats((@"D:\seat\work", "abcdef12")), @"D:\other"));

    [Fact]
    public void A_relative_config_dir_reads_snapshot_json()
        => Assert.Equal("snapshot.json", Name(Seats((@"D:\seat\work", "abcdef12")), @"seat\work"));

    [Fact]
    public void Missing_or_malformed_seats_read_snapshot_json()
    {
        Assert.Equal("snapshot.json", Name(null, null));
        Assert.Equal("snapshot.json", Name("{not json", null));
        Assert.Equal("snapshot.json", Name("[1,2]", null));
        Assert.Equal("snapshot.json", Name("{\"schema_version\":1}", null));
        Assert.Equal("snapshot.json", Name("{\"schema_version\":1,\"seats\":{}}", null));
    }

    [Fact]
    public void Not_schema_one_reads_snapshot_json()
    {
        var rows = "\"seats\":[{\"folder\":\"D:\\\\s\",\"account_ref\":\"abcdef12\"}]";
        Assert.Equal("snapshot.json", Name("{\"schema_version\":2," + rows + "}", @"D:\s"));
        Assert.Equal("snapshot.json", Name("{\"schema_version\":\"1\"," + rows + "}", @"D:\s"));
        Assert.Equal("snapshot.json", Name("{\"schema_version\":true," + rows + "}", @"D:\s"));
        Assert.Equal("snapshot.abcdef12.json", Name("{\"schema_version\":1," + rows + "}", @"D:\s"));
    }

    [Fact]
    public void Over_64_kb_reads_snapshot_json()
    {
        var pad = new string(' ', StatuslineSeatLookup.MaxBytes);
        Assert.Equal("snapshot.json", Name(Seats((@"D:\s", "abcdef12")) + pad, @"D:\s"));
    }

    [Fact]
    public void Rows_with_a_bad_ref_or_shape_are_skipped()
    {
        var text = "{\"schema_version\":1,\"seats\":[" +
            "{\"folder\":\"D:\\\\s\",\"account_ref\":\"ABCDEF12\"}," +
            "{\"folder\":\"D:\\\\s\",\"account_ref\":\"123\"}," +
            "{\"folder\":\"D:\\\\s\"}," +
            "{\"folder\":5,\"account_ref\":\"abcdef12\"}," +
            "\"junk\"," +
            "{\"folder\":\"D:\\\\s\",\"account_ref\":\"0a0b0c0d\"}]}";
        Assert.Equal("snapshot.0a0b0c0d.json", Name(text, @"D:\s"));
    }

    [Fact]
    public void The_first_matching_row_wins()
        => Assert.Equal("snapshot.11111111.json",
            Name(Seats((@"D:\s", "11111111"), (@"D:\s", "22222222")), @"D:\s"));
}
