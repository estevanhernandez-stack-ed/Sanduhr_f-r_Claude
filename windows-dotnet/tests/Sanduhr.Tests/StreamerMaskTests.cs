using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: the one formatter every surface calls (spec, Streamer mode). Off, it changes nothing and writes nothing.</summary>
public class StreamerMaskTests
{
    private const string Profile = @"C:\Users\qxuser";
    private static CodenameBook Book() => new(Enumerable.Repeat((byte)21, Codenames.SaltBytes).ToArray());
    private static StreamerMask OnMask(CodenameBook? book = null) => new(true, book ?? Book(), Profile);

    [Fact]
    public void Off_returns_every_name_as_it_is_and_assigns_nothing()
    {
        var book = Book();
        var off = new StreamerMask(false, book, Profile);
        Assert.Equal("Work", off.Name(CodenameCategory.Account, "Work"));
        Assert.Equal(@"~\.claude-work", off.Folder(Profile + @"\.claude-work"));
        Assert.Equal(".claude-work", off.FolderName(".claude-work"));
        Assert.Equal("https://qxhost.example/ingest", off.Endpoint("https://qxhost.example/ingest"));
        Assert.Equal(Profile + @"\AppData", off.Paths(Profile + @"\AppData"));
        Assert.Equal(0, book.Count);
        Assert.False(StreamerMask.Off.On);
        Assert.Equal("x", StreamerMask.Off.Name(CodenameCategory.Project, "x"));
    }

    [Fact]
    public void On_a_name_becomes_its_codename_and_keeps_it()
    {
        var mask = OnMask();
        var code = mask.Name(CodenameCategory.Account, "Qxalpha");
        Assert.True(Codenames.IsCodename(code));
        Assert.Equal(code, mask.Name(CodenameCategory.Account, "Qxalpha"));
        Assert.Equal(code, mask.Name(CodenameCategory.Account, "  Qxalpha "));   // trimmed before the lookup
        Assert.True(mask.Hides(CodenameCategory.Account, "Qxalpha"));
        Assert.Equal("", mask.Name(CodenameCategory.Account, ""));
        Assert.Equal("", mask.Name(CodenameCategory.Account, null));
    }

    [Fact]
    public void A_new_name_is_announced_once()
    {
        var mask = OnMask();
        var heard = new List<CodenameCategory>();
        mask.Assigned += heard.Add;
        mask.Name(CodenameCategory.Project, "a");
        mask.Name(CodenameCategory.Project, "a");
        mask.Folder(Profile + @"\.claude");
        Assert.Equal([CodenameCategory.Project, CodenameCategory.Folder], heard);
    }

    [Fact]
    public void A_shown_name_stays_real()
    {
        var book = Book();
        book.SetShown(CodenameCategory.Account, "Personal", true);
        book.SetShown(CodenameCategory.Folder, StreamerMask.FolderKey(Profile + @"\.claude"), true);
        var mask = OnMask(book);
        Assert.Equal("Personal", mask.Name(CodenameCategory.Account, "Personal"));
        Assert.False(mask.Hides(CodenameCategory.Account, "Personal"));
        Assert.Equal(@"~\.claude", mask.Folder(Profile + @"\.claude"));
        Assert.Equal(".claude", mask.FolderName(".claude"));
        Assert.Equal("sanduhr-meters@inline", mask.AddOn("sanduhr-meters@inline"));
        Assert.Equal(0, book.Count);
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public void A_folder_has_one_codename_however_its_path_is_written()
    {
        var mask = OnMask();
        var code = mask.Folder(Profile + @"\.claude-work");
        Assert.True(Codenames.IsCodename(code));
        Assert.Equal(code, mask.Folder(@"c:\users\QXUSER\.CLAUDE-WORK\"));
        Assert.Equal(code, mask.FolderName(".claude-work"));
        Assert.True(mask.HidesFolder(Profile + @"\.claude-work"));
    }

    /// <summary>Review Focus 1: labels are ordinal keys, folders are case-folded paths.</summary>
    [Fact]
    public void Case_differs_codename_differs_for_labels_but_not_for_folders()
    {
        var mask = OnMask();
        Assert.NotEqual(mask.Name(CodenameCategory.Account, "Dune"), mask.Name(CodenameCategory.Account, "dune"));
        Assert.Equal(mask.Folder(Profile + @"\.claude-A"), mask.Folder(Profile + @"\.claude-a"));
    }

    [Fact]
    public void An_add_on_id_masks_each_side_of_the_at()
    {
        var mask = OnMask();
        var shown = mask.AddOn("qxplugin@qxmarket");
        Assert.DoesNotContain("qx", shown);
        Assert.Matches(@"^[A-Z][a-z]+ [A-Z][a-z]+ ?\d*@[A-Z][a-z]+ [A-Z][a-z]+ ?\d*$", shown);
        Assert.Equal(mask.Name(CodenameCategory.AddOn, "qxplugin") + "@inline", mask.AddOn("qxplugin@inline"));
    }

    [Fact]
    public void Paths_show_the_profile_folder_as_USERPROFILE()
    {
        var mask = new StreamerMask(true, Book(), Profile, [Profile, @"D:\Home\qxother"]);
        Assert.Equal(@"%USERPROFILE%\AppData\Roaming\Sanduhr", mask.Paths(@"c:\users\qxuser\AppData\Roaming\Sanduhr"));
        Assert.Equal(@"copy failed: %USERPROFILE%\x.json", mask.Paths(@"copy failed: D:\Home\qxother\x.json"));
        Assert.Equal("no path here", mask.Paths("no path here"));
    }

    [Fact]
    public void An_endpoint_shows_its_host_as_a_codename_and_hides_its_path()
    {
        var mask = OnMask();
        var host = mask.Name(CodenameCategory.Host, "qxhost.example");
        Assert.Equal($"https://{host}/…", mask.Endpoint("https://qxhost.example/ingest?k=1"));
        Assert.Equal($"https://{host}", mask.Endpoint("https://qxhost.example"));
        Assert.Equal($"Redirected to {host}.", mask.HostsIn("Redirected to qxhost.example.", "https://qxhost.example/x"));
        Assert.Equal("", mask.Endpoint(""));
    }

    [Fact]
    public void A_runner_cards_title_names_the_runner_by_codename_and_keeps_the_job()
    {
        var mask = OnMask();
        var runner = new Watcher
        {
            Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner · build", Short = "build", Runner = "qxrunner", RunnerKey = "k", Job = "build", State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch,
        };
        var agent = new Watcher { Id = "a:1", Source = WatcherSource.Agent, Title = "Deploying qxproject", State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch };
        var shown = mask.Watchers([runner, agent]);
        Assert.Equal($"{mask.Name(CodenameCategory.Runner, "qxrunner")} · build", shown[0].Title);
        Assert.Equal("Deploying qxproject", shown[1].Title);   // Claude's own words: not a name Sanduhr can map (decision 11)
        Assert.Same(runner, new StreamerMask(false, Book(), Profile).Watchers([runner])[0]);
    }

    [Fact]
    public void The_weather_piece_shows_the_nickname_else_the_citys_codename()
    {
        var place = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        var mask = OnMask();
        Assert.Equal("Home", mask.WeatherName(place, "Home"));
        Assert.Equal(mask.Name(CodenameCategory.City, place.Label), mask.WeatherName(place, ""));
        Assert.Equal("", new StreamerMask(false, Book(), Profile).WeatherName(place, ""));
        Assert.Equal("", mask.WeatherName(null, ""));
    }

    [Fact]
    public void The_publish_sample_masks_folders_projects_and_the_pc_and_leaves_the_numbers()
    {
        var payload = new JsonObject
        {
            ["date"] = "2026-10-26",
            ["machine"] = "QXPC",
            ["homes"] = new JsonArray(".claude-qxwork"),
            ["totals"] = new JsonObject { ["tokens"] = 1200 },
            ["byProject"] = new JsonArray(new JsonObject { ["name"] = "qxproj", ["tokens"] = 1200 }),
        };
        var mask = OnMask();
        var shown = mask.PublishSample(payload, "this PC");
        var text = shown.ToJsonString();
        Assert.DoesNotContain("qx", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("this PC", (string?)shown["machine"]);
        Assert.Equal(mask.FolderName(".claude-qxwork"), (string?)shown["homes"]![0]);
        Assert.Equal(mask.Name(CodenameCategory.Project, "qxproj"), (string?)shown["byProject"]![0]!["name"]);
        Assert.Equal(1200, (int)shown["totals"]!["tokens"]!);
        Assert.Equal("QXPC", (string?)payload["machine"]);   // the record itself is never changed
    }

    // ---- Paths: error text reaches the screen with full paths (preflight P1) ----

    private static StreamerMask PathMask() => new(true, Book(), Profile, [Profile, @"D:\Home\qxother"]);

    [Theory]
    [InlineData(@"Access to the path 'C:\Users\qxuser\AppData\Roaming\Sanduhr\themes\x.json' is denied.", @"Access to the path '%USERPROFILE%\AppData\Roaming\Sanduhr\themes\x.json' is denied.")]
    [InlineData(@"Could not find a part of the path ""c:\USERS\QXUSER\AppData\x.json"".", @"Could not find a part of the path ""%USERPROFILE%\AppData\x.json"".")]
    [InlineData(@"failed: C:\Users\qxuser\x.json, then more", @"failed: %USERPROFILE%\x.json, then more")]
    [InlineData(@"failed (C:\Users\qxuser\x.json).", @"failed (%USERPROFILE%\x.json).")]
    [InlineData("failed C:/Users/qxuser/AppData/x.json now", @"failed %USERPROFILE%\AppData\x.json now")]
    [InlineData(@"json path C:\\Users\\qxuser\\x.json bad", @"json path %USERPROFILE%\x.json bad")]
    [InlineData(@"\\?\C:\Users\qxuser\x.json", @"%USERPROFILE%\x.json")]
    [InlineData(@"C:\Users\qxuser", @"%USERPROFILE%")]
    [InlineData(@"path ~\.claude\x.json gone", @"path %USERPROFILE%\.claude\x.json gone")]
    [InlineData(@"E:\Backups\qxuser\notes.txt", @"E:\…\notes.txt")]
    [InlineData(@"'D:\Clients\Acme Corp\plan.json' is locked", @"'D:\…\plan.json' is locked")]
    [InlineData(@"read D:\My Clients\Acme\plan.json to E:\out", @"read D:\…\plan.json to E:\…")]
    [InlineData(@"\\fileserver\finance$\q3\plan.json missing", @"\\…\plan.json missing")]
    [InlineData(@"""\\fileserver\finance$\q3 reports\plan.json"" missing", @"""\\…\plan.json"" missing")]
    [InlineData(@"C:\Users\Someone Else\Docs\x.json: denied", @"%USERPROFILE%\Docs\x.json: denied")]
    [InlineData("C:\\Users\\qxuser\\a.json\r\nD:\\Home\\qxother\\b.json", "%USERPROFILE%\\a.json\r\n%USERPROFILE%\\b.json")]
    public void Paths_masks_every_windows_path_shape_in_free_text(string text, string expected)
    {
        Assert.Equal(expected, PathMask().Paths(text));
    }

    [Fact]
    public void Paths_has_a_boundary_so_a_longer_name_is_not_cut_in_half()
    {
        var mask = new StreamerMask(true, Book(), @"C:\Users\ab", [@"C:\Users\ab"]);
        var shown = mask.Paths(@"C:\Users\abc\x.json and C:\Users\ab\y.json and C:\Users\ab-old\z.json");
        Assert.DoesNotContain("abc", shown);
        Assert.DoesNotContain("ab-old", shown);
        Assert.Contains(@"%USERPROFILE%\y.json", shown);
        Assert.DoesNotContain(@"%USERPROFILE%c", shown);
    }

    [Fact]
    public void Paths_masks_the_bare_windows_user_name_as_a_path_segment_and_nowhere_else()
    {
        var mask = PathMask();
        Assert.Equal(@"E:\…\x", mask.Paths(@"E:\data\qxuser\x"));
        Assert.Equal("the qxuser sample", mask.Paths("the qxuser sample"));   // not a path segment: left alone
        var real = new StreamerMask(true, Book(), @"C:\Users\" + Environment.UserName);
        Assert.DoesNotContain(Environment.UserName, real.Paths(@"C:\Users\" + Environment.UserName + @"\AppData"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, real.Paths(@"Z:\Elsewhere\" + Environment.UserName + @"\AppData"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Paths_is_stable_and_idempotent()
    {
        var mask = PathMask();
        foreach (var text in new[] { @"C:\Users\qxuser\a\b.json", @"D:\x\y\z.txt", @"\\srv\sh\f.txt", @"~\q", "no path" })
        {
            var once = mask.Paths(text);
            Assert.Equal(once, mask.Paths(once));
            Assert.Equal(once, mask.Paths(text));
        }
    }

    [Fact]
    public void Paths_masks_what_real_exceptions_say()
    {
        using var tmp = new TempDir();
        var home = Path.Combine(tmp.Path, "qxperson");
        Directory.CreateDirectory(home);
        var mask = new StreamerMask(true, Book(), home, [home]);
        var messages = new List<string>();

        // a missing folder (DirectoryNotFoundException), a locked file (IOException), a file where a folder should be, a missing file
        try { File.WriteAllText(Path.Combine(home, "nodir", "sub", "x.json"), "x"); } catch (Exception e) { messages.Add(e.Message); }
        var locked = Path.Combine(home, "locked.json");
        File.WriteAllText(locked, "x");
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { File.WriteAllText(locked, "y"); } catch (Exception e) { messages.Add(e.Message); }
        }
        var blocker = Path.Combine(home, "blocker");
        File.WriteAllText(blocker, "x");
        try { File.WriteAllText(Path.Combine(blocker, "x.json"), "x"); } catch (Exception e) { messages.Add(e.Message); }
        try { File.ReadAllText(Path.Combine(home, "missing.json")); } catch (Exception e) { messages.Add(e.Message); }

        Assert.Equal(4, messages.Count);
        foreach (var message in messages)
        {
            Assert.Contains("qxperson", message);   // the sentinel really is in the raw text
            var shown = mask.Paths(message);
            Assert.DoesNotContain("qxperson", shown);
            Assert.DoesNotContain(tmp.Path, shown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%USERPROFILE%", shown);
        }
    }

    // ---- HostsIn: the publish result (preflight P2) ----

    [Fact]
    public void A_publish_result_masks_every_host_shaped_word_not_only_the_endpoints()
    {
        var mask = OnMask();
        var other = mask.Name(CodenameCategory.Host, "qxother.example");
        Assert.Equal($"Redirected (307) to {other}.", mask.HostsIn("Redirected (307) to qxother.example.", "https://qxhost.example/ingest"));
        Assert.Equal($"Redirected (307) to {other}.", mask.HostsIn("Redirected (307) to QXOTHER.example.", null));
        Assert.Equal($"moved to https://{other}/path now", mask.HostsIn("moved to https://qxother.example/path now", null));
        Assert.DoesNotContain("192.168.4.7", mask.HostsIn("could not reach 192.168.4.7", null));
    }

    [Fact]
    public void A_rejected_result_drops_the_servers_body()
    {
        var mask = OnMask();
        Assert.Equal("Rejected (400).", mask.HostsIn("Rejected (400): qxsecret says go to qxcorp.example", "https://qxhost.example/x"));
        Assert.Equal("Rejected (503).", mask.HostsIn("Rejected (503): {\"a\":\n\"b\"}", null));
    }

    [Fact]
    public void Host_masking_leaves_a_file_name_in_a_masked_path_alone()
    {
        var mask = OnMask();
        Assert.Equal(@"could not write D:\…\plan.json", mask.HostsIn(@"could not write D:\Clients\Acme\plan.json", "https://qxhost.example"));
        Assert.Equal("no hosts here", mask.HostsIn("no hosts here", null));
    }

    [Fact]
    public void Off_the_publish_result_is_untouched()
    {
        Assert.Equal("Rejected (400): qxcorp.example", StreamerMask.Off.HostsIn("Rejected (400): qxcorp.example", "https://qxcorp.example"));
    }

    [Fact]
    public void Agent_and_automatic_cards_have_their_paths_masked_but_their_words_kept()
    {
        var mask = new StreamerMask(true, Book(), Profile);
        var agent = new Watcher { Id = "a:1", Source = WatcherSource.Agent, Title = @"npm test in C:\Users\qxuser\repo", Note = @"C:\Users\qxuser\repo\log.txt", State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch };
        var shown = mask.Watchers([agent])[0];
        Assert.Equal(@"npm test in %USERPROFILE%\repo", shown.Title);
        Assert.Equal(@"%USERPROFILE%\repo\log.txt", shown.Note);
    }
}
