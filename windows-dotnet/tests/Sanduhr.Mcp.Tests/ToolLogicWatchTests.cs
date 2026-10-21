using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>watch_start, watch_update, watch_end: refused while the user's switch is off, validated
/// like the Mac, and queued as request files the widget's own decoder accepts. Everything runs against
/// a temp folder, never the real profile.</summary>
public class ToolLogicWatchTests
{
    private static McpConfig WatchConfig(TempDir tmp) => new()
    {
        SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
        VaultDir = Path.Combine(tmp.Path, "vault"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        ExternalDir = Path.Combine(tmp.Path, "ext"),
    };

    private static ToolLogic On(TempDir tmp)
    {
        var cfg = WatchConfig(tmp);
        Directory.CreateDirectory(cfg.ExternalDir!);
        File.WriteAllBytes(Path.Combine(cfg.ExternalDir!, WatcherRequest.SwitchFile), WatcherRequest.SwitchJson(true));
        return new ToolLogic(cfg, () => Now);
    }

    private static string[] Requests(TempDir tmp)
    {
        string dir = Path.Combine(tmp.Path, "ext");
        return Directory.Exists(dir) ? Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray() : [];
    }

    private static JsonObject Args(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static WatcherRequest.Decoded Read(TempDir tmp, string file) =>
        WatcherRequest.Decode(File.ReadAllBytes(Path.Combine(tmp.Path, "ext", file)), Now)
        ?? throw new InvalidOperationException("the widget's decoder refused " + file);

    [Fact]
    public void Switch_off_or_missing_refuses_with_watchers_off_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var logic = new ToolLogic(WatchConfig(tmp), () => Now);
        var r = logic.BuildWatchStart(Args("""{"title":"CI"}"""));
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("watchers_off", (string?)r["reason"]);
        Assert.Contains("Do not retry", (string?)r["remedy"]);
        Assert.Empty(Requests(tmp));

        Directory.CreateDirectory(Path.Combine(tmp.Path, "ext"));
        File.WriteAllBytes(Path.Combine(tmp.Path, "ext", WatcherRequest.SwitchFile), WatcherRequest.SwitchJson(false));
        Assert.Equal("watchers_off", (string?)logic.BuildWatchStart(Args("""{"title":"CI"}"""))["reason"]);
        Assert.Equal("watchers_off", (string?)logic.BuildWatchUpdate(Args("""{"id":"w0123456789ab"}"""))["reason"]);
        Assert.Equal("watchers_off", (string?)logic.BuildWatchEnd(Args("""{"id":"w0123456789ab","result":"passed"}"""))["reason"]);
        Assert.Equal([WatcherRequest.SwitchFile], Requests(tmp));
    }

    [Theory]
    [InlineData("""{"agents":true}""")]                          // no schema_version
    [InlineData("""{"schema_version":2,"agents":true}""")]       // another schema
    [InlineData("""{"schema_version":1,"agents":"yes"}""")]      // not a boolean
    [InlineData("not json")]
    public void A_switch_file_that_is_not_exactly_on_reads_as_off(string content)
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "ext"));
        File.WriteAllText(Path.Combine(tmp.Path, "ext", WatcherRequest.SwitchFile), content);
        var r = new ToolLogic(WatchConfig(tmp), () => Now).BuildWatchStart(Args("""{"title":"CI"}"""));
        Assert.Equal("watchers_off", (string?)r["reason"]);
    }

    [Fact]
    public void Start_queues_a_request_the_widgets_own_decoder_reads()
    {
        using var tmp = new TempDir();
        var r = On(tmp).BuildWatchStart(Args("""
            {"title":"  PR 140 CI: combine statuslines  ","short":"PR 140","link":"https://github.com/o/r/actions/runs/1","total":12,"work":true}
            """));

        Assert.Equal("ok", (string?)r["status"]);
        string id = (string)r["id"]!;
        Assert.Matches("^w[0-9a-f]{12}$", id);
        Assert.Contains("watch_end", (string?)r["note"]);

        var file = Assert.Single(Requests(tmp), f => f!.StartsWith("watch-request-"));
        Assert.Matches($"^watch-request-\\d{{13}}-000001-{id}\\.json$", file);
        var decoded = Read(tmp, file!);
        var start = Assert.IsType<WatcherCommand.Start>(decoded.Command);
        Assert.Equal(id, start.Id);
        Assert.Equal("PR 140 CI: combine statuslines", start.Title);
        Assert.Equal("PR 140", start.Short);
        Assert.Equal("https://github.com/o/r/actions/runs/1", start.Link!.AbsoluteUri);
        Assert.Equal(12, start.Total);
        Assert.True(start.Work);
        // No folder path travels in the file.
        Assert.Null(decoded.Folder);
        Assert.DoesNotContain("folder", File.ReadAllText(Path.Combine(tmp.Path, "ext", file!)));
    }

    [Fact]
    public void Requests_are_written_atomically_and_leave_no_temp_file()
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        logic.BuildWatchStart(Args("""{"title":"one"}"""));
        logic.BuildWatchStart(Args("""{"title":"two"}"""));
        var files = Requests(tmp).Where(f => f != WatcherRequest.SwitchFile).ToArray();
        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.EndsWith(".json", f));
        Assert.Contains("-000001-", files[0]);
        Assert.Contains("-000002-", files[1]);
    }

    [Theory]
    [InlineData("""{}""", "title is required")]
    [InlineData("""{"title":"   "}""", "title must not be empty")]
    [InlineData("""{"title":5}""", "title must be a string")]
    [InlineData("""{"title":"two\nlines"}""", "title must be one line of at most 80 characters")]
    [InlineData("""{"title":"short","short":"thirteen char"}""", "short must be one line of at most 12 characters")]
    [InlineData("""{"title":"x","link":"http://example.com/run"}""", "link must be an https:// link")]
    [InlineData("""{"title":"x","link":"https://user:pw@example.com/run"}""", "link must be an https:// link")]
    [InlineData("""{"title":"x","link":"https://exa mple.com"}""", "link must be an https:// link")]
    [InlineData("""{"title":"x","link":5}""", "link must be an https:// link")]
    [InlineData("""{"title":"x","total":0}""", "total must be a whole number from 1 to 1000000")]
    [InlineData("""{"title":"x","total":1000001}""", "total must be a whole number from 1 to 1000000")]
    [InlineData("""{"title":"x","total":true}""", "total must be a whole number from 1 to 1000000")]
    [InlineData("""{"title":"x","total":2.5}""", "total must be a whole number from 1 to 1000000")]
    [InlineData("""{"title":"x","work":"yes"}""", "work must be true or false")]
    public void Start_refuses_bad_input_with_invalid_params_and_writes_nothing(string args, string expected)
    {
        using var tmp = new TempDir();
        var r = On(tmp).BuildWatchStart(Args(args));
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("invalid_params", (string?)r["reason"]);
        Assert.Contains(expected, (string?)r["remedy"]);
        Assert.Equal([WatcherRequest.SwitchFile], Requests(tmp));
    }

    [Fact]
    public void Title_at_80_characters_passes_and_81_does_not()
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        Assert.Equal("ok", (string?)logic.BuildWatchStart(Args($$"""{"title":"{{new string('t', 80)}}"}"""))["status"]);
        Assert.Equal("invalid_params", (string?)logic.BuildWatchStart(Args($$"""{"title":"{{new string('t', 81)}}"}"""))["reason"]);
    }

    [Fact]
    public void Missing_arguments_are_a_typed_refusal_not_a_crash()
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        Assert.Equal("invalid_params", (string?)logic.BuildWatchStart(null)["reason"]);
        Assert.Equal("invalid_params", (string?)logic.BuildWatchUpdate(null)["reason"]);
        Assert.Equal("invalid_params", (string?)logic.BuildWatchEnd(null)["reason"]);
        Assert.Equal("rejected", (string?)new ToolLogic(WatchConfig(tmp), () => Now).BuildProposeDeskMessages(null)["status"]);
    }

    [Fact]
    public void An_unknown_argument_is_named_and_refused()
    {
        using var tmp = new TempDir();
        var r = On(tmp).BuildWatchStart(Args("""{"title":"x","colour":"red"}"""));
        Assert.Equal("invalid_params", (string?)r["reason"]);
        Assert.Contains("colour", (string?)r["remedy"]);
        Assert.Contains("Takes title, short, link, total, work", (string?)r["remedy"]);
    }

    [Fact]
    public void Update_moves_a_started_watcher_and_end_finishes_it()
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        string id = (string)logic.BuildWatchStart(Args("""{"title":"deploy","total":10}"""))["id"]!;

        var u = logic.BuildWatchUpdate(Args($$"""{"id":"{{id}}","done":3,"note":"lint passed","state":"waiting"}"""));
        Assert.Equal("ok", (string?)u["status"]);
        Assert.Equal(id, (string?)u["id"]);

        var e = logic.BuildWatchEnd(Args($$"""{"id":"{{id}}","result":"failed","note":"tests red"}"""));
        Assert.Equal("ok", (string?)e["status"]);

        var files = Requests(tmp).Where(f => f != WatcherRequest.SwitchFile).ToArray();
        Assert.Equal(3, files.Length);
        var update = Assert.IsType<WatcherCommand.Update>(Read(tmp, files[1]).Command);
        Assert.Equal((id, 3, "lint passed", WatcherState.Waiting), (update.Id, update.Done, update.Note, update.State));
        var end = Assert.IsType<WatcherCommand.End>(Read(tmp, files[2]).Command);
        Assert.Equal((id, WatcherState.Failed, "tests red"), (end.Id, end.Result, end.Note));

        var after = logic.BuildWatchUpdate(Args($$"""{"id":"{{id}}","done":4}"""));
        Assert.Equal("ended", (string?)after["reason"]);
        Assert.Equal("ended", (string?)logic.BuildWatchEnd(Args($$"""{"id":"{{id}}","result":"passed"}"""))["reason"]);
        Assert.Equal(3, Requests(tmp).Count(f => f != WatcherRequest.SwitchFile));
    }

    [Fact]
    public void Update_and_end_refuse_an_id_this_session_did_not_start()
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        Assert.Equal("unknown_id", (string?)logic.BuildWatchUpdate(Args("""{"id":"w0123456789ab"}"""))["reason"]);
        Assert.Equal("unknown_id", (string?)logic.BuildWatchEnd(Args("""{"id":"w0123456789ab","result":"passed"}"""))["reason"]);
        Assert.Equal("invalid_params", (string?)logic.BuildWatchUpdate(Args("""{"id":"nope"}"""))["reason"]);
        Assert.Equal("invalid_params", (string?)logic.BuildWatchUpdate(Args("""{"done":1}"""))["reason"]);
        Assert.Equal([WatcherRequest.SwitchFile], Requests(tmp));
    }

    [Theory]
    [InlineData("""{"done":-1}""", "done must be a whole number from 0 to 1000000")]
    [InlineData("""{"done":"3"}""", "done must be a whole number from 0 to 1000000")]
    [InlineData("""{"state":"passed"}""", "state must be running or waiting")]
    [InlineData("""{"state":7}""", "state must be running or waiting")]
    [InlineData("""{"note":"two\nlines"}""", "note must be one line of at most 140 characters")]
    public void Update_refuses_bad_input(string extra, string expected)
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        string id = (string)logic.BuildWatchStart(Args("""{"title":"x"}"""))["id"]!;
        var args = Args(extra);
        args["id"] = id;
        var r = logic.BuildWatchUpdate(args);
        Assert.Equal("invalid_params", (string?)r["reason"]);
        Assert.Contains(expected, (string?)r["remedy"]);
        Assert.Single(Requests(tmp), f => f!.StartsWith("watch-request-"));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"result":"done"}""")]
    [InlineData("""{"result":true}""")]
    public void End_needs_passed_or_failed(string extra)
    {
        using var tmp = new TempDir();
        var logic = On(tmp);
        string id = (string)logic.BuildWatchStart(Args("""{"title":"x"}"""))["id"]!;
        var args = Args(extra);
        args["id"] = id;
        var r = logic.BuildWatchEnd(args);
        Assert.Equal("invalid_params", (string?)r["reason"]);
        Assert.Contains("result must be passed or failed", (string?)r["remedy"]);
        // A refused end does not end it: a good one still can.
        Assert.Equal("ok", (string?)logic.BuildWatchEnd(Args($$"""{"id":"{{id}}","result":"passed"}"""))["status"]);
    }

    [Fact]
    public void A_server_without_a_folder_is_a_typed_unavailable()
    {
        using var tmp = new TempDir();
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = Path.Combine(tmp.Path, "vault"),
            ConsentedRoots = Array.Empty<(string, string)>(),
            RootsFound = Array.Empty<string>(),
        };
        var r = new ToolLogic(cfg, () => Now).BuildWatchStart(Args("""{"title":"x"}"""));
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("unavailable", (string?)r["reason"]);
    }

    [Fact]
    public void The_watch_file_names_match_what_the_widget_watches()
    {
        Assert.Equal("watch-request-*.json", WatcherRequest.RequestPattern);
        Assert.Equal("watchers.json", WatcherRequest.SwitchFile);
        Assert.Equal("watch-request-1790000000000-000007-w0123456789ab.json",
            WatcherRequest.RequestFileName(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000), 7, "w0123456789ab"));
    }
}
