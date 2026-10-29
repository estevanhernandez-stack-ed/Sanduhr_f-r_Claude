using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: ~\.sanduhr\streamer.json, the statusline's half (spec, "Two locations, on purpose"). No real name goes in.</summary>
public class StreamerFileTests
{
    [Fact]
    public void A_folders_ref_is_the_account_ref_of_its_lower_case_full_path()
    {
        var a = StreamerFile.FolderRef(@"C:\Users\qxuser\.claude-work");
        Assert.Matches("^[0-9a-f]{8}$", a);
        Assert.Equal(a, StreamerFile.FolderRef(@"c:\users\QXUSER\.CLAUDE-WORK\"));
        Assert.Equal(SnapshotContract.AccountRef(@"c:\users\qxuser\.claude-work"), a);
        Assert.Equal("", StreamerFile.FolderRef(""));
    }

    [Fact]
    public void On_it_holds_codenames_and_shown_refs_and_no_real_name()
    {
        var work = StreamerFile.FolderRef(@"C:\Users\qxuser\.claude-qxwork");
        var home = StreamerFile.FolderRef(@"C:\Users\qxuser\.claude");
        var json = StreamerFile.Build(true, [new StreamerFolder(work, "Dune Fox", false), new StreamerFolder(home, null, true)]);
        Assert.Equal(1, (int)json["schema_version"]!);
        Assert.True((bool)json["on"]!);
        Assert.Equal("Dune Fox", (string?)json["folders"]![work]);
        Assert.Equal([home], json["shown"]!.AsArray().Select(n => (string)n!));
        Assert.DoesNotContain("qx", json.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Off_it_says_off_and_lists_nothing()
    {
        var json = StreamerFile.Build(false, [new StreamerFolder("abcd1234", "Dune Fox", false)]);
        Assert.False((bool)json["on"]!);
        Assert.Empty(json["folders"]!.AsObject());
        Assert.Empty(json["shown"]!.AsArray());
    }

    [Fact]
    public void A_ref_that_is_not_eight_hex_digits_is_left_out()
    {
        var json = StreamerFile.Build(true, [new StreamerFolder("", "Dune Fox", false), new StreamerFolder("ABCD1234", "Mesa Owl", false)]);
        Assert.Empty(json["folders"]!.AsObject());
    }

    [Fact]
    public void It_is_written_atomically_as_utf8_without_a_bom()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, ".sanduhr", StreamerFile.FileName);
        Assert.True(StreamerFile.Write(path, StreamerFile.Build(true, [])));
        Assert.False(File.Exists(path + ".tmp"));
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.True((bool)JsonNode.Parse(Encoding.UTF8.GetString(bytes))!["on"]!);
    }

    [Fact]
    public void The_two_files_live_where_the_spec_puts_them()
    {
        using var tmp = new TempDir();
        var paths = new Paths(Path.Combine(tmp.Path, "roaming"), Path.Combine(tmp.Path, "home"), Path.Combine(tmp.Path, "local"));
        Assert.Equal(Path.Combine(paths.AppDataDir, "codenames.json"), paths.CodenamesFile);
        Assert.Equal(Path.Combine(paths.ExternalDirPath, "streamer.json"), paths.StreamerStateFile);
    }
}
