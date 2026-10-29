using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>propose_now_playing_looks: the widget's own checks first (shape, then the contrast rule against the wallpaper in
/// desk-state.json), then the request handoff. A refused request leaves no file. Temp folders only.</summary>
public class ToolLogicNowPlayingLooksTests
{
    /// <summary>The fake widget's read: waits for the request and retries while the server's rename into place still holds
    /// it (a sharing violation on a Windows runner would kill the task and leave the server to time out).</summary>
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

    private static McpConfig LooksConfig(TempDir tmp, TimeSpan? wait = null) => new()
    {
        SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
        VaultDir = Path.Combine(tmp.Path, "vault"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        DesignStatePath = Path.Combine(tmp.Path, "ext", DeskDesign.StateFile),
        DesignRequestPath = Path.Combine(tmp.Path, "ext", DeskDesign.RequestFile),
        DesignResultPath = Path.Combine(tmp.Path, "ext", DeskDesign.ResultFile),
        DesignWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        DesignPollInterval = TimeSpan.FromMilliseconds(20),
        LooksRequestPath = Path.Combine(tmp.Path, "ext", NowPlayingLookProposal.RequestFile),
        LooksResultPath = Path.Combine(tmp.Path, "ext", NowPlayingLookProposal.ResultFile),
        LooksWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        LooksPollInterval = TimeSpan.FromMilliseconds(20),
    };

    private static JsonObject Args(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static string Look(string title = "Blinding Lights", string artist = "The Weeknd", string ink = """["#ff2a6d","#05d9e8"]""", string extra = "") =>
        $$"""{"title":"{{title}}","artist":"{{artist}}","ink":{{ink}}{{extra}}}""";

    private static JsonObject Propose(McpConfig cfg, params string[] looks) =>
        new ToolLogic(cfg, () => Now).BuildProposeNowPlayingLooks(Args("{\"looks\":[" + string.Join(",", looks) + "]}"));

    private static void AssertNoRequest(McpConfig cfg) => Assert.False(File.Exists(cfg.LooksRequestPath!), "a refused request must leave no file");

    /// <summary>A desk-state.json whose wallpaper has this brightness at every anchor, the now-playing piece at its default place.</summary>
    private static void WriteState(McpConfig cfg, double brightness)
    {
        var anchors = new JsonObject();
        foreach (var a in DeskAnchors.All)
            anchors[DeskAnchors.Word(a)] = new JsonObject { ["brightness"] = brightness, ["busyness"] = 0.1 };
        var state = new JsonObject
        {
            ["schema_version"] = 1,
            ["writer_version"] = "3.7.0",
            ["design"] = Args("""{"layout":{"clock":{"anchor":"bl","size":1},"nowPlaying":{"anchor":"bl","size":1}},"ink":"auto","plate":false,"shadow":true}"""),
            ["wallpaper"] = new JsonObject
            {
                ["path"] = @"C:\Users\me\Pictures\wall.jpg",
                ["summary"] = new JsonObject
                {
                    ["brightness"] = brightness,
                    ["dominant"] = new JsonArray((JsonNode)new JsonObject { ["hex"] = "#101010", ["share"] = 1.0 }),
                    ["monitors"] = new JsonArray((JsonNode)new JsonObject
                    {
                        ["index"] = 0, ["primary"] = true, ["width"] = 2560, ["height"] = 1440, ["brightness"] = brightness,
                        ["dominant"] = new JsonArray(), ["anchors"] = anchors,
                    }),
                },
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DesignStatePath!)!);
        File.WriteAllText(cfg.DesignStatePath!, state.ToJsonString());
    }

    // -- the checks ----------------------------------------------------------

    [Fact]
    public async Task A_good_request_is_queued_for_the_widget_and_its_answer_comes_back()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp, Waits.AnswerWait);
        var logic = new ToolLogic(cfg, () => Now);

        // Play the widget: take the request, check it with the widget's own decoder, answer pending.
        var widget = Task.Run(async () =>
        {
            var decoded = NowPlayingLookProposal.Decode(await ReadWhenReady(cfg.LooksRequestPath!), Now);
            Assert.Equal(MessageDecodedKind.Proposal, decoded.Kind);
            var p = decoded.Proposal!;
            Assert.Equal("for tonight", p.Note);
            var look = Assert.Single(p.Looks);
            Assert.Equal(SongKey.Of("Blinding Lights", "The Weeknd"), look.Key);
            Assert.Equal(["#ff2a6d", "#05d9e8"], look.Look.Ink);
            Assert.Equal(LookStyle.Bold, look.Look.Style);
            File.Delete(cfg.LooksRequestPath!);
            File.WriteAllBytes(cfg.LooksResultPath!, NowPlayingLookProposal.ResultJson(p.Id, MessageProposalStatus.PendingApproval, styleOn: true, now: Now));
        });

        var r = logic.BuildProposeNowPlayingLooks(Args($$"""{"looks":[{{Look(extra: ""","style":"Bold","mood":"neon" """)}}],"note":"  for tonight  "}"""));
        await widget;

        Assert.Equal("pending_approval", (string?)r["status"]);
        Assert.True((bool)r["style_on"]!);
        Assert.Matches("^[0-9a-f]{32}$", (string?)r["request_id"]);
        Assert.Null(r["looks_saved"]);
        Assert.Null(r["reasons"]);
    }

    [Fact]
    public async Task An_applied_answer_carries_looks_saved_and_style_on_and_a_rejected_one_its_reasons()
    {
        foreach (bool applied in new[] { true, false })
        {
            using var tmp = new TempDir();
            var cfg = LooksConfig(tmp, Waits.AnswerWait);
            var widget = Task.Run(async () =>
            {
                var p = NowPlayingLookProposal.Decode(await ReadWhenReady(cfg.LooksRequestPath!), Now).Proposal!;
                File.WriteAllBytes(cfg.LooksResultPath!, applied
                    ? NowPlayingLookProposal.ResultJson(p.Id, MessageProposalStatus.Applied, styleOn: false, saved: 2, now: Now)
                    : NowPlayingLookProposal.ResultJson(p.Id, MessageProposalStatus.Rejected, styleOn: true, ["dismissed by the user"], now: Now));
            });

            var r = Propose(cfg, Look(), Look("Starboy"));
            await widget;

            if (applied)
            {
                Assert.Equal("applied", (string?)r["status"]);
                Assert.Equal(2, (int)r["looks_saved"]!);
                Assert.False((bool)r["style_on"]!);
            }
            else
            {
                Assert.Equal("rejected", (string?)r["status"]);
                Assert.Equal("dismissed by the user", (string?)r["reasons"]![0]);
                Assert.True((bool)r["style_on"]!);
            }
        }
    }

    [Fact]
    public void A_bad_look_is_refused_with_numbered_reasons_and_leaves_no_request_file()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        var r = Propose(cfg, Look(), Look(ink: """["nope"]"""), """{"ink":["#ffffff"]}""", Look(extra: ""","style":"sans" """));

        Assert.Equal("rejected", (string?)r["status"]);
        var reasons = ((JsonArray)r["reasons"]!).Select(x => (string?)x).ToList();
        Assert.Contains(reasons, x => x!.StartsWith("look 2 ink must be 1 to 4 hex colors", StringComparison.Ordinal));
        Assert.Contains(reasons, x => x!.StartsWith("look 3 title must be one line", StringComparison.Ordinal));
        Assert.Contains(reasons, x => x!.StartsWith("look 4 style must be one of: plain, bold, italic, bolditalic, smallcaps", StringComparison.Ordinal));
        Assert.DoesNotContain(reasons, x => x!.StartsWith("look 1 ", StringComparison.Ordinal));
        AssertNoRequest(cfg);
    }

    [Theory]
    [InlineData("""{"looks":[]}""", "looks must be a list of 1 to 20 looks")]
    [InlineData("""{}""", "looks must be a list of 1 to 20 looks")]
    [InlineData("""{"looks":"x"}""", "looks must be a list of 1 to 20 looks")]
    [InlineData("""{"looks":[{"title":"x","ink":["#ffffff"]}],"colour":"red"}""", "unknown argument(s): colour")]
    [InlineData("""{"looks":[{"title":"x","ink":["#ffffff"]}],"note":5}""", "note must be a string")]
    [InlineData("""{"looks":[{"title":"x","ink":["#ffffff"]}],"note":"a\nb"}""", "note must be one line")]
    public void Bad_arguments_are_refused(string args, string reason)
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        var r = new ToolLogic(cfg, () => Now).BuildProposeNowPlayingLooks(Args(args));
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Contains(((JsonArray)r["reasons"]!).Select(x => (string?)x), x => x!.StartsWith(reason, StringComparison.Ordinal));
        AssertNoRequest(cfg);
    }

    [Fact]
    public void More_than_twenty_looks_are_refused()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        var r = Propose(cfg, Enumerable.Range(0, 21).Select(i => Look("Song " + i)).ToArray());
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Contains("1 to 20", (string?)r["reasons"]![0]);
        AssertNoRequest(cfg);
    }

    // -- the contrast rule ----------------------------------------------------

    [Fact]
    public void A_color_too_dark_for_the_spot_is_refused_before_anything_is_sent()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        WriteState(cfg, brightness: 0.03);

        var r = Propose(cfg, Look(), Look("Starboy", ink: """["#ff2a6d","#202020"]"""));

        Assert.Equal("rejected", (string?)r["status"]);
        var reason = Assert.Single(((JsonArray)r["reasons"]!).Select(x => (string?)x));
        Assert.StartsWith("look 2 color #202020 is too dark for this spot (", reason, StringComparison.Ordinal);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void A_pale_color_on_a_bright_wallpaper_is_too_light_and_a_fitting_one_goes()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        WriteState(cfg, brightness: 0.92);

        var pale = Propose(cfg, Look(ink: """["#ffffe0"]"""));
        Assert.Equal("rejected", (string?)pale["status"]);
        Assert.Contains("too light for this spot", (string?)pale["reasons"]![0]);
        AssertNoRequest(cfg);

        var dark = Propose(cfg, Look(ink: """["#1b2a6b"]"""));
        Assert.Equal("queued", (string?)dark["status"]);   // nobody answers in this test; it got past the checks
        Assert.True(File.Exists(cfg.LooksRequestPath!));
    }

    [Fact]
    public void Without_a_desk_state_the_spot_is_not_judged_here_and_the_widget_decides()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        var r = Propose(cfg, Look(ink: """["#101010"]"""));
        Assert.Equal("queued", (string?)r["status"]);
        Assert.True(File.Exists(cfg.LooksRequestPath!));
    }

    // -- a design that brings looks -------------------------------------------

    [Fact]
    public void A_design_may_bring_looks_and_they_meet_the_same_checks()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        WriteState(cfg, brightness: 0.03);
        var logic = new ToolLogic(cfg, () => Now);

        string Design(string looks) => "{\"design\":{\"plate\":true,\"looks\":[" + looks + "]}}";

        var bad = logic.BuildProposeDesk(Args(Design(Look(ink: """["nope"]"""))));
        Assert.Equal("rejected", (string?)bad["status"]);
        Assert.Equal("looks", (string?)bad["reasons"]![0]!["field"]);
        Assert.StartsWith("look 1 ink must be", (string?)bad["reasons"]![0]!["message"], StringComparison.Ordinal);

        var dim = logic.BuildProposeDesk(Args(Design(Look(ink: """["#202020"]"""))));
        Assert.Equal("rejected", (string?)dim["status"]);
        Assert.Contains("too dark for this spot", (string?)dim["reasons"]![0]!["message"]);
        Assert.False(File.Exists(cfg.DesignRequestPath!));

        var good = logic.BuildProposeDesk(Args(Design(Look())));
        Assert.Equal("queued", (string?)good["status"]);   // nobody answers in this test; it passed the checks
        var request = (JsonObject)JsonNode.Parse(File.ReadAllText(cfg.DesignRequestPath!))!;
        Assert.Equal("Blinding Lights", (string?)request["design"]!["looks"]![0]!["title"]);
    }

    // -- the handoff ----------------------------------------------------------

    [Fact]
    public void A_stale_answer_for_another_request_is_not_taken_and_a_silent_widget_is_queued()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.LooksResultPath!)!);
        File.WriteAllBytes(cfg.LooksResultPath!, NowPlayingLookProposal.ResultJson("someone-elses-id", MessageProposalStatus.Applied, true, saved: 1, now: Now));

        var r = Propose(cfg, Look());

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.NotNull(r["request_id"]);
        Assert.True(File.Exists(cfg.LooksRequestPath!));   // it stays for the widget's next look
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(cfg.LooksRequestPath!)!, "*.tmp"));
    }

    [Fact]
    public void The_request_file_is_exactly_what_the_widgets_decoder_takes()
    {
        using var tmp = new TempDir();
        var cfg = LooksConfig(tmp);
        Propose(cfg, Look(extra: ""","style":"smallcaps","mood":"  night drive " """), Look("Starboy", "The Weeknd"));

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(cfg.LooksRequestPath!))!;
        Assert.Equal(1, (int)root["schema_version"]!);
        Assert.Equal(["schema_version", "id", "requested_at", "looks", "note"], root.Select(kv => kv.Key).ToArray());
        Assert.Null(root["note"]);
        var first = (JsonObject)root["looks"]![0]!;
        Assert.Equal(["title", "artist", "ink", "style", "mood"], first.Select(kv => kv.Key).ToArray());
        Assert.Equal("smallcaps", (string?)first["style"]);
        Assert.Equal("night drive", (string?)first["mood"]);
        var decoded = NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), Now);
        Assert.Equal(MessageDecodedKind.Proposal, decoded.Kind);
        Assert.Equal((string?)root["id"], decoded.Id);
        Assert.Equal(2, decoded.Proposal!.Looks.Count);
    }

    [Fact]
    public void The_file_names_are_the_widgets_and_Resolve_wires_them_to_the_external_folder()
    {
        var cfg = McpConfig.Resolve(@"C:\h", @"C:\h\.sanduhr", @"C:\h\AppData\Roaming\Sanduhr", @"C:\h\AppData\Local", _ => null);
        Assert.Equal(@"C:\h\.sanduhr\now-playing-looks-request.json", cfg.LooksRequestPath);
        Assert.Equal(@"C:\h\.sanduhr\now-playing-looks-result.json", cfg.LooksResultPath);
        Assert.Equal(TimeSpan.FromSeconds(10), cfg.LooksWaitTimeout);
    }

    [Fact]
    public void An_unwired_server_says_so_after_the_checks_pass()
    {
        using var tmp = new TempDir();
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = Path.Combine(tmp.Path, "vault"),
            ConsentedRoots = Array.Empty<(string, string)>(),
            RootsFound = Array.Empty<string>(),
        };
        var r = new ToolLogic(cfg, () => Now).BuildProposeNowPlayingLooks(Args("{\"looks\":[" + Look() + "]}"));
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("unavailable", (string?)r["reason"]);
    }

    [Fact]
    public void The_tool_is_listed_with_its_arguments_and_the_trigger_in_its_description()
    {
        var tool = (JsonObject)ToolCatalog.Build().Single(t => (string?)t!["name"] == "propose_now_playing_looks")!;
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.False((bool)tool["annotations"]!["openWorldHint"]!);
        var props = (JsonObject)tool["inputSchema"]!["properties"]!;
        Assert.Equal(["looks", "note"], props.Select(kv => kv.Key).ToArray());
        var item = (JsonObject)props["looks"]!["items"]!["properties"]!;
        Assert.Equal(["title", "artist", "ink", "style", "mood"], item.Select(kv => kv.Key).ToArray());
        Assert.Equal(["plain", "bold", "italic", "bolditalic", "smallcaps"], item["style"]!["enum"]!.AsArray().Select(x => (string)x!).ToArray());
        string description = (string)tool["description"]!;
        foreach (var word in new[] { "pending_approval", "looks_saved", "style_on", "queued", "3:1" })
            Assert.Contains(word, description);
        Assert.True(description.All(c => c < 128), "descriptions are plain ASCII");
    }
}
