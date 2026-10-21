using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>get_desk_messages (reads messages.txt and the widget's state file) and
/// propose_desk_messages (the widget's own checks first, then the request handoff). Temp folders only.</summary>
public class ToolLogicDeskMessagesTests
{
    /// <summary>The fake widget's read: waits for the request and retries while the server's rename
    /// into place still holds it (a sharing violation on a Windows runner would kill the task and
    /// leave the server to time out).</summary>
    private static async Task<byte[]> ReadWhenReady(string path)
    {
        while (true)
        {
            await Task.Delay(10);
            if (!File.Exists(path)) continue;
            try { return File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static McpConfig DeskConfig(TempDir tmp, TimeSpan? wait = null) => new()
    {
        SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
        VaultDir = Path.Combine(tmp.Path, "vault"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        DeskRequestPath = Path.Combine(tmp.Path, "ext", MessageProposal.RequestFile),
        DeskResultPath = Path.Combine(tmp.Path, "ext", MessageProposal.ResultFile),
        DeskStatePath = Path.Combine(tmp.Path, "ext", MessageProposal.StateFile),
        DeskMessagesPaths = [Path.Combine(tmp.Path, "cache", "messages.txt"), Path.Combine(tmp.Path, "app", "messages.txt")],
        DeskWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        DeskPollInterval = TimeSpan.FromMilliseconds(20),
    };

    private static JsonObject Args(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>The real file is the second candidate: what a non-Store install has.</summary>
    private static void WriteMessages(McpConfig cfg, string text, int candidate = 1)
    {
        string path = cfg.DeskMessagesPaths![candidate];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteState(McpConfig cfg, string? pinned, string rotate = "daily", bool mix = false,
        MessageSpecialMode special = MessageSpecialMode.Stack, double seconds = 10)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DeskStatePath!)!);
        File.WriteAllBytes(cfg.DeskStatePath!, MessageProposal.StateJson(pinned, rotate, mix, special, seconds));
    }

    // -- get_desk_messages ----------------------------------------------------

    [Fact]
    public void Get_without_a_file_or_state_reports_the_defaults()
    {
        using var tmp = new TempDir();
        var r = new ToolLogic(DeskConfig(tmp), () => Now).BuildDeskMessages();

        Assert.Equal("ok", (string?)r["status"]);
        Assert.False(r["file_found"]!.GetValue<bool>());
        Assert.Empty((JsonArray)r["lines"]!);
        Assert.False(r["pinned"]!.GetValue<bool>());
        Assert.Equal("daily", (string?)r["rotate"]);
        Assert.False(r["mix_daily"]!.GetValue<bool>());
        Assert.Equal("stack", (string?)r["special_mode"]);
        Assert.Equal(10, (int)r["special_seconds"]!);
        Assert.Null(r["today"]);
        Assert.Empty((JsonArray)r["today_special"]!);
        Assert.Equal(60, (int)r["limits"]!["lines_per_proposal"]!);
        Assert.Equal(120, (int)r["limits"]!["characters_per_line"]!);
        Assert.Contains("not reported", (string?)r["settings_note"]);
    }

    [Fact]
    public void Get_reads_every_raw_line_picks_today_and_stacks_the_dates_lines()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        var local = Now.ToLocalTime();
        string date = local.ToString("MM-dd", CultureInfo.InvariantCulture);
        WriteMessages(cfg, $"# notes\r\nkeep building.\r\n\r\n{date}: {{glow}} happy day.\r\n\r\n\r\n");
        WriteState(cfg, pinned: null, special: MessageSpecialMode.Turns, seconds: 30, mix: true);

        var r = new ToolLogic(cfg, () => Now).BuildDeskMessages();

        Assert.True(r["file_found"]!.GetValue<bool>());
        // Raw lines, comments and effects included, trailing blanks dropped.
        Assert.Equal(["# notes", "keep building.", "", $"{date}: {{glow}} happy day."],
            ((JsonArray)r["lines"]!).Select(l => (string)l!).ToArray());
        Assert.Equal("keep building.", (string?)r["today"]);
        Assert.Equal(["{glow} happy day."], ((JsonArray)r["today_special"]!).Select(l => (string)l!).ToArray());
        Assert.Equal("turns", (string?)r["special_mode"]);
        Assert.Equal(30, (int)r["special_seconds"]!);
        Assert.True(r["mix_daily"]!.GetValue<bool>());
        Assert.Null(r["settings_note"]);
    }

    [Fact]
    public void The_store_builds_cache_copy_shadows_the_real_file()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        WriteMessages(cfg, "the real one.\n", candidate: 1);
        Assert.Equal("the real one.", (string?)new ToolLogic(cfg, () => Now).BuildDeskMessages()["today"]);

        WriteMessages(cfg, "the cache copy.\n", candidate: 0);
        var r = new ToolLogic(cfg, () => Now).BuildDeskMessages();
        Assert.Equal("the cache copy.", (string?)r["today"]);
        Assert.Equal(["the cache copy."], ((JsonArray)r["lines"]!).Select(l => (string)l!).ToArray());
    }

    [Fact]
    public void A_pinned_line_is_the_usual_line_with_or_without_the_file()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        WriteState(cfg, pinned: "stay with it.", rotate: "hourly");

        var noFile = new ToolLogic(cfg, () => Now).BuildDeskMessages();
        Assert.False(noFile["file_found"]!.GetValue<bool>());
        Assert.True(noFile["pinned"]!.GetValue<bool>());
        Assert.Equal("hourly", (string?)noFile["rotate"]);
        Assert.Equal("stay with it.", (string?)noFile["today"]);

        WriteMessages(cfg, "keep building.\n");
        var withFile = new ToolLogic(cfg, () => Now).BuildDeskMessages();
        Assert.Equal("stay with it.", (string?)withFile["today"]);
    }

    [Theory]
    [InlineData("""{"schema_version":2,"pinned":true,"pinned_line":"x"}""")]
    [InlineData("""not json""")]
    public void A_state_file_of_another_schema_or_unreadable_is_the_defaults(string state)
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DeskStatePath!)!);
        File.WriteAllText(cfg.DeskStatePath!, state);
        var r = new ToolLogic(cfg, () => Now).BuildDeskMessages();
        Assert.False(r["pinned"]!.GetValue<bool>());
        Assert.NotNull(r["settings_note"]);
    }

    [Fact]
    public void Get_refuses_a_file_that_is_too_big_or_not_utf8()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        string messages = cfg.DeskMessagesPaths![1];
        Directory.CreateDirectory(Path.GetDirectoryName(messages)!);

        File.WriteAllBytes(messages, new byte[ToolLogic.DeskMaxFileBytes + 1].Select(_ => (byte)'a').ToArray());
        var big = new ToolLogic(cfg, () => Now).BuildDeskMessages();
        Assert.Equal("no_data", (string?)big["status"]);
        Assert.Equal("too_large", (string?)big["reason"]);

        File.WriteAllBytes(messages, [0x68, 0xFF, 0xFE, 0x69]);
        var bad = new ToolLogic(cfg, () => Now).BuildDeskMessages();
        Assert.Equal("not_utf8", (string?)bad["reason"]);
    }

    // -- propose_desk_messages: the checks ------------------------------------

    [Theory]
    [InlineData("""{}""", "lines must be a list of 1 to 60 strings")]
    [InlineData("""{"lines":[]}""", "lines must be a list of 1 to 60 strings")]
    [InlineData("""{"lines":"hello"}""", "lines must be a list of 1 to 60 strings")]
    [InlineData("""{"lines":["ok.",5]}""", "line 2 is not a string")]
    [InlineData("""{"lines":["# only a comment"]}""", "no message line: every line is blank or a # comment")]
    [InlineData("""{"lines":["Thur: late."]}""", "line 1 starts with 'Thur:'; write the day as Mon, Tue, Wed, Thu, Fri, Sat or Sun")]
    [InlineData("""{"lines":["13-40: nope."]}""", "line 1 starts with '13-40:', which is not a date; write MM-DD, like 10-31")]
    [InlineData("""{"lines":["{ink:red} hi."]}""", "line 1 {ink:...} has a color that is not hex (#rgb or #rrggbb)")]
    [InlineData("""{"lines":["{glow}"]}""", "line 1 the line has effects but no text")]
    [InlineData("""{"lines":["two\nlines"]}""", "line 1 has a control character or a line break")]
    [InlineData("""{"lines":["ok."],"mode":"swap"}""", "mode must be add or replace")]
    [InlineData("""{"lines":["ok."],"note":5}""", "note must be a string")]
    [InlineData("""{"lines":["ok."],"extra":1}""", "unknown argument(s): extra")]
    public void Propose_refuses_what_the_widget_would_and_writes_nothing(string args, string reason)
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        var r = new ToolLogic(cfg, () => Now).BuildProposeDeskMessages(Args(args));

        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Contains(reason, ((JsonArray)r["reasons"]!).Select(x => (string?)x));
        Assert.False(File.Exists(cfg.DeskRequestPath!));
    }

    [Fact]
    public void Propose_refuses_too_many_lines_a_long_line_and_a_long_note()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        var logic = new ToolLogic(cfg, () => Now);

        string many = string.Join(",", Enumerable.Range(0, 61).Select(i => $"\"line {i}.\""));
        var tooMany = logic.BuildProposeDeskMessages(Args($$"""{"lines":[{{many}}]}"""));
        Assert.Contains("61 lines; the limit is 60", ((JsonArray)tooMany["reasons"]!).Select(x => (string?)x));

        var longLine = logic.BuildProposeDeskMessages(Args($$"""{"lines":["{{new string('a', 121)}}"]}"""));
        Assert.Contains("line 1 is 121 characters; the limit is 120", ((JsonArray)longLine["reasons"]!).Select(x => (string?)x));
        Assert.Equal("queued", (string?)logic.BuildProposeDeskMessages(Args($$"""{"lines":["{{new string('a', 120)}}"]}"""))["status"]);

        File.Delete(cfg.DeskRequestPath!);
        var longNote = logic.BuildProposeDeskMessages(Args($$"""{"lines":["ok."],"note":"{{new string('n', 301)}}"}"""));
        Assert.Contains("note must be one line of at most 300 characters", ((JsonArray)longNote["reasons"]!).Select(x => (string?)x));
        Assert.False(File.Exists(cfg.DeskRequestPath!));
    }

    [Fact]
    public void The_reasons_are_the_widgets_own_text()
    {
        // The server calls Core's Validate, so what it says is what the widget would say.
        using var tmp = new TempDir();
        var r = new ToolLogic(DeskConfig(tmp), () => Now).BuildProposeDeskMessages(Args("""{"lines":["Thur: late.","ok."]}"""));
        var expected = MessageProposal.Validate(["Thur: late.", "ok."]);
        Assert.Equal(expected, ((JsonArray)r["reasons"]!).Select(x => (string)x!).ToArray());
    }

    // -- propose_desk_messages: the handoff -----------------------------------

    [Fact]
    public async Task A_good_proposal_is_queued_for_the_widget_and_its_answer_comes_back()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp, TimeSpan.FromSeconds(5));
        var logic = new ToolLogic(cfg, () => Now);

        // Play the widget: take the request, check it with the widget's own decoder, answer pending.
        var widget = Task.Run(async () =>
        {
            var decoded = MessageProposal.Decode(await ReadWhenReady(cfg.DeskRequestPath!), Now);
            Assert.Equal(MessageDecodedKind.Proposal, decoded.Kind);
            var p = decoded.Proposal!;
            Assert.Equal(["03-14: {ink:#ff7e5f,#feb47b} happy birthday, Sam.", "keep going."], p.Lines);
            Assert.Equal(MessageProposalMode.Add, p.Mode);
            Assert.Equal("for Sam", p.Note);
            File.Delete(cfg.DeskRequestPath!);
            File.WriteAllBytes(cfg.DeskResultPath!, MessageProposal.ResultJson(p.Id, MessageProposalStatus.PendingApproval, p.Mode, now: Now));
        });

        var r = logic.BuildProposeDeskMessages(Args("""
            {"lines":["  03-14: {ink:#ff7e5f,#feb47b} happy birthday, Sam.  ","keep going."],"note":"  for Sam  "}
            """));
        await widget;

        Assert.Equal("pending_approval", (string?)r["status"]);
        Assert.Equal("add", (string?)r["mode"]);
        Assert.Matches("^[0-9a-f]{32}$", (string?)r["request_id"]);
        Assert.Null(r["lines_added"]);
    }

    [Fact]
    public async Task An_applied_or_rejected_answer_carries_its_counts_and_reasons()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp, TimeSpan.FromSeconds(5));
        var logic = new ToolLogic(cfg, () => Now);
        var widget = Task.Run(async () =>
        {
            var p = MessageProposal.Decode(await ReadWhenReady(cfg.DeskRequestPath!), Now).Proposal!;
            File.WriteAllBytes(cfg.DeskResultPath!, MessageProposal.ResultJson(p.Id, MessageProposalStatus.Applied,
                p.Mode, applied: new MessageApplied("x\n", 1, 1), now: Now));
        });

        var r = logic.BuildProposeDeskMessages(Args("""{"lines":["a.","b."],"mode":"replace"}"""));
        await widget;

        Assert.Equal("applied", (string?)r["status"]);
        Assert.Equal("replace", (string?)r["mode"]);
        Assert.Equal(1, (int)r["lines_added"]!);
        Assert.Equal(1, (int)r["lines_skipped"]!);
    }

    [Fact]
    public void A_stale_answer_for_another_request_is_not_taken_and_a_silent_widget_is_queued()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DeskResultPath!)!);
        File.WriteAllBytes(cfg.DeskResultPath!, MessageProposal.ResultJson("someone-elses-id", MessageProposalStatus.Applied, MessageProposalMode.Add, now: Now));

        var r = new ToolLogic(cfg, () => Now).BuildProposeDeskMessages(Args("""{"lines":["ok."]}"""));

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.NotNull(r["request_id"]);
        Assert.True(File.Exists(cfg.DeskRequestPath!));   // it stays for the widget's next look
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(cfg.DeskRequestPath!)!, "*.tmp"));
    }

    [Fact]
    public void The_request_file_is_exactly_what_the_widgets_decoder_takes()
    {
        using var tmp = new TempDir();
        var cfg = DeskConfig(tmp);
        new ToolLogic(cfg, () => Now).BuildProposeDeskMessages(Args("""{"lines":["Mon: one thing at a time.","fine."],"mode":"add"}"""));

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(cfg.DeskRequestPath!))!;
        Assert.Equal(1, (int)root["schema_version"]!);
        Assert.Equal(["schema_version", "id", "requested_at", "lines", "mode", "note"], root.Select(kv => kv.Key).ToArray());
        Assert.Null(root["note"]);
        var decoded = MessageProposal.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), Now);
        Assert.Equal(MessageDecodedKind.Proposal, decoded.Kind);
        Assert.Equal((string?)root["id"], decoded.Id);
    }

    [Fact]
    public void The_file_names_are_the_widgets()
    {
        Assert.Equal("desk-messages-request.json", MessageProposal.RequestFile);
        Assert.Equal("desk-messages-result.json", MessageProposal.ResultFile);
        Assert.Equal("desk-messages-state.json", MessageProposal.StateFile);
        var cfg = McpConfig.Resolve(@"C:\h", @"C:\h\.sanduhr", @"C:\h\AppData\Roaming\Sanduhr", @"C:\h\AppData\Local", _ => null);
        Assert.Equal(@"C:\h\.sanduhr\desk-messages-request.json", cfg.DeskRequestPath);
        Assert.Equal(@"C:\h\.sanduhr\desk-messages-result.json", cfg.DeskResultPath);
        Assert.Equal(@"C:\h\.sanduhr\desk-messages-state.json", cfg.DeskStatePath);
        // The Store build's cache copy first, then the real file.
        Assert.Equal(
            [@"C:\h\AppData\Local\Packages\626LabsLLC.SanduhrfrClaude_wz1chhb2h2v4a\LocalCache\Roaming\Sanduhr\messages.txt",
             @"C:\h\AppData\Roaming\Sanduhr\messages.txt"],
            cfg.DeskMessagesPaths);
        Assert.Equal(@"C:\h\.sanduhr", cfg.ExternalDir);
    }
}
