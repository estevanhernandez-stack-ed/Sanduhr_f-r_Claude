using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: the switch, the book, the coalesced writes and streamer.json (spec, Streamer mode; Reset).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerServiceTests : AppWindowFixture
{
    private (StreamerService Service, Paths Paths, List<Action> Posted) Make(Func<bool>? resetDone = null)
    {
        var paths = ScratchPaths();
        var posted = new List<Action>();
        Home(".claude-qxwork");
        var service = new StreamerService(paths, new SettingsStore(paths),
            () => ClaudeCodeFolders.Build(Profile, null, []), () => Profile, post: posted.Add, resetDone: resetDone ?? (() => false));
        return (service, paths, posted);
    }

    private static void RunPosted(List<Action> posted)
    {
        var now = posted.ToList();
        posted.Clear();
        foreach (var a in now) a();
    }

    [Fact]
    public void Absent_is_off_and_off_writes_no_file()
    {
        var (service, paths, _) = Make();
        // SettingsStore's constructor creates the shared folder (for mcp-settings.json); the service must not bring it back.
        if (Directory.Exists(paths.ExternalDirPath)) Directory.Delete(paths.ExternalDirPath, true);
        service.Load();
        Assert.False(service.On);
        Assert.False(StreamerMode.On);
        Assert.False(Directory.Exists(paths.ExternalDirPath), "asking for the path must not create the shared folder");
        Assert.False(new SettingsStore(paths).LoadStreamerMode());
        Assert.False(File.Exists(paths.CodenamesFile));
        Assert.False(File.Exists(paths.StreamerStateFile));
        Assert.Equal("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    [Fact]
    public void Turning_it_on_saves_the_key_masks_and_maps_every_known_folder_for_the_statusline()
    {
        var (service, paths, _) = Make();
        service.Load();
        int changed = 0;
        service.Changed += () => changed++;
        service.SetOn(true);

        Assert.Equal(1, changed);
        Assert.True(new SettingsStore(paths).LoadStreamerMode());
        Assert.True(StreamerMode.On);
        Assert.Same(service.Mask, StreamerMode.Mask);
        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        Assert.True((bool)json["on"]!);
        var work = StreamerFile.FolderRef(Path.Combine(Profile, ".claude-qxwork"));
        var plain = StreamerFile.FolderRef(Path.Combine(Profile, ".claude"));
        Assert.Equal(service.Mask.Folder(Path.Combine(Profile, ".claude-qxwork")), (string?)json["folders"]![work]);
        Assert.NotNull(json["folders"]![plain]);
        Assert.DoesNotContain("qx", File.ReadAllText(paths.StreamerStateFile), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(paths.CodenamesFile));
    }

    [Fact]
    public void A_codename_survives_a_restart()
    {
        var (service, paths, posted) = Make();
        service.Load();
        service.SetOn(true);
        var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        RunPosted(posted);
        service.Dispose();

        var again = new StreamerService(paths, new SettingsStore(paths), () => ClaudeCodeFolders.Build(Profile, null, []), () => Profile, post: _ => { }, resetDone: () => false);
        again.Load();
        Assert.True(again.On);
        Assert.Equal(code, again.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    /// <summary>Decision 10: a burst of new names is one write, on the next dispatcher turn.</summary>
    [Fact]
    public void A_burst_of_new_names_is_written_once()
    {
        var (service, paths, posted) = Make();
        service.Load();
        service.SetOn(true);
        var stamp = File.GetLastWriteTimeUtc(paths.CodenamesFile);
        for (int i = 0; i < 50; i++)
            StreamerMode.Mask.Name(CodenameCategory.Project, "qxproject-" + i);
        Assert.Single(posted);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(paths.CodenamesFile));
        RunPosted(posted);
        var book = Codenames.Load(paths.CodenamesFile)!;
        Assert.True(book.TryGet(CodenameCategory.Project, "qxproject-49", out _));
    }

    [Fact]
    public void Turning_it_off_keeps_the_book_and_tells_the_statusline()
    {
        var (service, paths, _) = Make();
        service.Load();
        service.SetOn(true);
        var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        service.SetOn(false);
        Assert.False(StreamerMode.On);
        Assert.Equal("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        Assert.False((bool)JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!["on"]!);
        service.SetOn(true);
        Assert.Equal(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    [Fact]
    public void Shuffle_renames_everything_keeps_the_allowlist_and_rewrites_streamer_json()
    {
        var (service, paths, _) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");
        var before = StreamerMode.Mask.Folder(folder);
        service.SetShown(CodenameCategory.Account, "Personal", true);
        int changed = 0;
        service.Changed += () => changed++;
        service.Shuffle();
        Assert.Equal(1, changed);
        Assert.NotEqual(before, StreamerMode.Mask.Folder(folder));
        Assert.Equal("Personal", StreamerMode.Mask.Name(CodenameCategory.Account, "Personal"));
        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        Assert.Equal(StreamerMode.Mask.Folder(folder), (string?)json["folders"]![StreamerFile.FolderRef(folder)]);
    }

    [Fact]
    public void Showing_a_folders_real_name_moves_it_to_shown_in_streamer_json()
    {
        var (service, paths, _) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");
        service.SetShown(CodenameCategory.Folder, StreamerMask.FolderKey(folder), true);
        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        var reference = StreamerFile.FolderRef(folder);
        Assert.Contains(reference, json["shown"]!.AsArray().Select(n => (string)n!));
        Assert.Null(json["folders"]![reference]);
        Assert.Equal(@"~\.claude-qxwork", StreamerMode.Mask.Folder(folder));
    }

    [Fact]
    public void The_allowlist_can_be_set_while_off_and_entries_list_it()
    {
        var (service, _, _) = Make();
        service.Load();
        service.SetShown(CodenameCategory.Account, "Personal", true);
        Assert.Contains(service.Entries(), e => e.Category == CodenameCategory.Account && e.Real == "Personal" && e.Shown);
        Assert.Contains(service.Entries(), e => e.Real == "sanduhr-meters" && e.Shown);
    }

    /// <summary>Review Focus 5, slice 6's P14 rule: after Reset, nothing writes either file back.</summary>
    [Fact]
    public void After_reset_the_service_writes_nothing()
    {
        bool reset = false;
        var (service, paths, posted) = Make(() => reset);
        service.Load();
        service.SetOn(true);
        File.Delete(paths.CodenamesFile);
        File.Delete(paths.StreamerStateFile);
        reset = true;
        StreamerMode.Mask.Name(CodenameCategory.Project, "late");
        RunPosted(posted);
        service.SetShown(CodenameCategory.Account, "x", true);
        service.Dispose();
        Assert.False(File.Exists(paths.CodenamesFile));
        Assert.False(File.Exists(paths.StreamerStateFile));
    }

    [Fact]
    public void The_widget_owns_one_and_its_dispose_flushes()
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            Assert.NotNull(widget.Streamer);
            Assert.False(widget.Streamer.On);
            widget.Streamer.SetOn(true);
            Assert.True(StreamerMode.On);
        }
        finally { widget.Dispose(); }
    }
}
