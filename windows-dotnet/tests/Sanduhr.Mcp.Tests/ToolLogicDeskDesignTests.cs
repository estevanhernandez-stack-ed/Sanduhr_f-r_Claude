using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>get_desk (reads the widget's desk-state.json) and propose_desk (the widget's own design checks first, then the
/// request handoff). Temp folders only.</summary>
public class ToolLogicDeskDesignTests
{
    private const string AppId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

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

    private static McpConfig DesignConfig(TempDir tmp, TimeSpan? wait = null) => new()
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
    };

    private static JsonObject Args(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static JsonObject Propose(McpConfig cfg, string designJson, string extra = "") =>
        new ToolLogic(cfg, () => Now).BuildProposeDesk(Args("{\"design\":" + designJson + extra + "}"));

    /// <summary>Anchors with a bright top right and a dark everything else.</summary>
    private static JsonObject Wallpaper(double dark, double bright)
    {
        var anchors = new JsonObject();
        foreach (var a in DeskAnchors.All)
            anchors[DeskAnchors.Word(a)] = new JsonObject { ["brightness"] = a == DeskAnchor.TR ? bright : dark, ["busyness"] = 0.1 };
        return new JsonObject
        {
            ["brightness"] = dark,
            ["dominant"] = new JsonArray((JsonNode)new JsonObject { ["hex"] = "#101010", ["share"] = 1.0 }),
            ["monitors"] = new JsonArray((JsonNode)new JsonObject
            {
                ["index"] = 0, ["primary"] = true, ["width"] = 2560, ["height"] = 1440, ["brightness"] = dark,
                ["dominant"] = new JsonArray(), ["anchors"] = anchors,
            }),
        };
    }

    private static void WriteState(McpConfig cfg, Action<JsonObject>? edit = null)
    {
        var state = new JsonObject
        {
            ["schema_version"] = 1,
            ["writer_version"] = "3.7.0",
            ["design"] = Args("""{"layout":{"clock":{"anchor":"bl","size":1},"claude":{"anchor":"bl","size":1},"meters":{"hidden":true}},"font":"EsteFont Pro","ink":"auto","plate":false,"shadow":true,"launcher":{"orientation":"row","buttons":[]}}"""),
            ["pieces"] = new JsonArray("clock", "claude", "meters"),
            ["anchors"] = new JsonArray("tl", "tc", "tr", "ml", "mr", "bl", "bc", "br"),
            ["fonts"] = new JsonObject
            {
                ["installed"] = new JsonArray("Segoe UI", "Caveat"),
                ["bundled"] = new JsonArray("EsteFont Pro", "EsteFont 26"),
            },
            ["wallpaper"] = new JsonObject { ["path"] = @"C:\Users\me\Pictures\wall.jpg", ["summary"] = Wallpaper(0.04, 0.9) },
            ["designs"] = new JsonArray(),
            ["direct"] = false,
            ["limits"] = new JsonObject { ["contrast_min"] = 3.0 },
            ["start_menu_apps"] = new JsonArray((JsonNode)new JsonObject { ["id"] = AppId, ["name"] = "Calculator" }),
            ["secret"] = "not passed on",
        };
        edit?.Invoke(state);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DesignStatePath!)!);
        File.WriteAllText(cfg.DesignStatePath!, state.ToJsonString());
    }

    /// <summary>The Obsidian built-in as the palette JSON propose_theme takes.</summary>
    private static JsonObject ObsidianTheme()
    {
        var t = ThemeCatalog.BuiltIns["obsidian"];
        return new JsonObject
        {
            ["name"] = t.Name, ["bg"] = t.Bg, ["glass"] = t.Glass, ["glass_on_mica"] = t.GlassOnMica, ["title_bg"] = t.TitleBg,
            ["border"] = t.Border, ["footer_bg"] = t.FooterBg, ["bar_bg"] = t.BarBg, ["text"] = t.Text, ["text_secondary"] = t.TextSecondary,
            ["text_dim"] = t.TextDim, ["text_muted"] = t.TextMuted, ["accent"] = t.Accent, ["pace_marker"] = t.PaceMarker, ["sparkline"] = t.Sparkline,
            ["glass_alpha"] = t.GlassAlpha, ["border_alpha"] = t.BorderAlpha,
        };
    }

    private const string MainId = @"\\?\DISPLAY#GSM5B09#5&2c9d3a1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string SideId = @"\\?\DISPLAY#DEL4167#5&2c9d3a1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    /// <summary>A state that lists two monitors, as the widget writes them.</summary>
    private static void WriteTwoMonitors(McpConfig cfg) => WriteState(cfg, s => s["monitors"] = new JsonArray(
        (JsonNode)new JsonObject { ["id"] = MainId, ["name"] = "3440\u00D71440, main", ["width"] = 3440, ["height"] = 1440, ["primary"] = true, ["left"] = 0, ["top"] = 0 },
        (JsonNode)new JsonObject { ["id"] = SideId, ["name"] = "1920\u00D71080, right of main", ["width"] = 1920, ["height"] = 1080, ["primary"] = false, ["left"] = 3440, ["top"] = 0 }));

    private static void AssertNoRequest(McpConfig cfg) => Assert.False(File.Exists(cfg.DesignRequestPath!), "a refused design must leave no request file");

    // -- get_desk -------------------------------------------------------------

    [Fact]
    public void Get_without_a_state_file_says_to_start_the_widget()
    {
        using var tmp = new TempDir();
        var r = new ToolLogic(DesignConfig(tmp), () => Now).BuildGetDesk();

        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("missing", (string?)r["reason"]);
        Assert.Contains("Start the Sanduhr widget", (string?)r["remedy"]);
        Assert.Null(r["design"]);
    }

    [Fact]
    public void Get_on_an_unwired_server_is_unavailable()
    {
        var cfg = new McpConfig
        {
            SnapshotPath = "x", VaultDir = "x", ConsentedRoots = Array.Empty<(string, string)>(), RootsFound = Array.Empty<string>(),
        };
        var r = new ToolLogic(cfg, () => Now).BuildGetDesk();
        Assert.Equal("unavailable", (string?)r["reason"]);
    }

    [Fact]
    public void Get_returns_the_widgets_state_and_nothing_it_did_not_promise()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);
        File.SetLastWriteTimeUtc(cfg.DesignStatePath!, Now.UtcDateTime.AddSeconds(-42));

        var r = new ToolLogic(cfg, () => Now).BuildGetDesk();

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(42, (int)r["state_age_seconds"]!);
        Assert.Equal("3.7.0", (string?)r["writer_version"]);
        Assert.Equal("EsteFont Pro", (string?)r["design"]!["font"]);
        Assert.Equal("bl", (string?)r["design"]!["layout"]!["clock"]!["anchor"]);
        Assert.Equal(3, r["pieces"]!.AsArray().Count);
        Assert.Equal(8, r["anchors"]!.AsArray().Count);
        Assert.Equal("Caveat", (string?)r["fonts"]!["installed"]![1]);
        Assert.Equal(@"C:\Users\me\Pictures\wall.jpg", (string?)r["wallpaper"]!["path"]);
        Assert.Equal(0.9, (double)r["wallpaper"]!["summary"]!["monitors"]![0]!["anchors"]!["tr"]!["brightness"]!);
        Assert.False((bool)r["direct"]!);
        Assert.Equal(AppId, (string?)r["start_menu_apps"]![0]!["id"]);
        Assert.Null(r["secret"]);
    }

    [Fact]
    public void Get_reads_a_state_the_widget_still_has_open_for_writing()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);
        using var open = new FileStream(cfg.DesignStatePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal("ok", (string?)new ToolLogic(cfg, () => Now).BuildGetDesk()["status"]);
    }

    [Theory]
    [InlineData("not json", "malformed")]
    [InlineData("[]", "malformed")]
    [InlineData("{\"schema_version\":2}", "schema_unsupported")]
    [InlineData("{\"design\":{}}", "schema_unsupported")]
    public void Get_names_what_is_wrong_with_a_state_it_cannot_use(string content, string reason)
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DesignStatePath!)!);
        File.WriteAllText(cfg.DesignStatePath!, content);
        var r = new ToolLogic(cfg, () => Now).BuildGetDesk();
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal(reason, (string?)r["reason"]);
    }

    [Fact]
    public void Get_will_not_read_a_state_over_a_megabyte()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DesignStatePath!)!);
        File.WriteAllText(cfg.DesignStatePath!, "{\"schema_version\":1,\"pad\":\"" + new string('x', ToolLogic.DesignMaxStateBytes) + "\"}");
        Assert.Equal("too_large", (string?)new ToolLogic(cfg, () => Now).BuildGetDesk()["reason"]);
    }

    // -- propose_desk: refusals leave no request file --------------------------

    [Fact]
    public void An_unknown_argument_is_refused_without_a_request_file()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var r = new ToolLogic(cfg, () => Now).BuildProposeDesk(Args("""{"design":{},"apply":true}"""));
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("arguments", (string?)r["reasons"]![0]!["field"]);
        AssertNoRequest(cfg);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"design":"dusk"}""")]
    [InlineData("""{"design":[]}""")]
    [InlineData("""{"design":null}""")]
    public void A_missing_or_misshapen_design_is_refused(string args)
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var r = new ToolLogic(cfg, () => Now).BuildProposeDesk(Args(args));
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("design", (string?)r["reasons"]![0]!["field"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void A_null_argument_set_is_refused_not_crashed()
    {
        using var tmp = new TempDir();
        var r = new ToolLogic(DesignConfig(tmp), () => Now).BuildProposeDesk(null);
        Assert.Equal("rejected", (string?)r["status"]);
    }

    [Theory]
    [InlineData("""{"plate":"yes"}""", "plate")]
    [InlineData("""{"ink":"red"}""", "ink")]
    [InlineData("""{"layout":{"toaster":{"anchor":"bl"}}}""", "layout.toaster")]
    [InlineData("""{"layout":{"clock":{"anchor":"middle"}}}""", "layout.clock.anchor")]
    [InlineData("""{"layout":{"clock":{"size":9}}}""", "layout.clock.size")]
    [InlineData("""{"font":"C:\\Windows\\Fonts\\evil.ttf"}""", "font")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"cmd.exe /c calc"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"C:\\Windows\\..\\calc.exe"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"file:///C:/Windows/System32/calc.exe"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"javascript:alert(1)"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"http://example.com"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"messages":{"lines":["Thur: smoke."]}}""", "messages.lines")]
    [InlineData("""{"theme":{"name":"x"}}""", "theme.bg")]
    [InlineData("""{"name":"line one\nline two"}""", "name")]
    [InlineData("""{"looks":{"a":1}}""", "looks")]
    [InlineData("""{"command":"calc.exe"}""", "design")]
    public void A_bad_design_is_refused_with_its_field_and_leaves_no_request_file(string design, string field)
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var r = Propose(cfg, design);

        Assert.Equal("rejected", (string?)r["status"]);
        var fields = r["reasons"]!.AsArray().Select(n => (string?)n!["field"]).ToList();
        Assert.Contains(field, fields);
        Assert.All(r["reasons"]!.AsArray(), n => Assert.False(string.IsNullOrEmpty((string?)n!["message"])));
        Assert.Null(r["request_id"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void A_runaway_design_is_refused_without_a_request_file()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var r = new ToolLogic(cfg, () => Now).BuildProposeDesk(new JsonObject
        {
            ["design"] = new JsonObject { ["note"] = new string('x', 500_000) },
        });
        Assert.Equal("rejected", (string?)r["status"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void An_app_the_start_menu_does_not_list_is_refused_when_the_widget_has_told_us_the_list()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);

        var r = Propose(cfg, """{"launcher":{"buttons":[{"kind":"app","target":"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\cmd.exe"}]}}""");
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("launcher.buttons[0].target", (string?)r["reasons"]![0]!["field"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void Ink_that_cannot_be_read_over_the_wallpaper_is_refused_naming_the_piece_and_anchor()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);   // dark wallpaper, bright top right

        var r = Propose(cfg, """{"ink":"light","layout":{"clock":{"anchor":"tr"}}}""");
        Assert.Equal("rejected", (string?)r["status"]);
        var message = (string?)r["reasons"]![0]!["message"];
        Assert.Contains("clock", message);
        Assert.Contains("tr", message);
        Assert.Contains("3:1", message);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void An_ink_change_is_judged_against_the_pieces_already_on_the_desk()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);   // clock and claude sit at the dark bottom left

        Assert.Equal("queued", (string?)Propose(cfg, """{"ink":"light"}""")["status"]);   // dark ground: fine
        File.Delete(cfg.DesignRequestPath!);
        var r = Propose(cfg, """{"ink":"dark"}""");   // dark ink on dark ground
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("ink", (string?)r["reasons"]![0]!["field"]);
    }

    // -- propose_desk: the handoff ---------------------------------------------

    [Fact]
    public async Task A_good_design_is_queued_normalized_and_the_widgets_answer_comes_back()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp, TimeSpan.FromSeconds(5));
        WriteState(cfg);
        var logic = new ToolLogic(cfg, () => Now);

        var widget = Task.Run(async () =>
        {
            var request = (JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.DesignRequestPath!))!;
            Assert.Equal(1, (int)request["schema_version"]!);
            Assert.Matches("^[0-9a-f]{32}$", (string?)request["id"]);
            Assert.Equal("2026-07-26T12:00:00.000Z", (string?)request["requested_at"]);
            var design = (JsonObject)request["design"]!;
            Assert.Equal("Dusk", (string?)design["name"]);          // trimmed
            Assert.Equal("Warm and low.", (string?)design["note"]);
            Assert.Equal("#ffaa00", (string?)design["ink"]);        // normalized
            Assert.Equal(1.2, (double)design["layout"]!["clock"]!["size"]!);
            // The server marks nothing the user did not ask for.
            Assert.Null(design["plate"]);
            File.Delete(cfg.DesignRequestPath!);
            File.WriteAllText(cfg.DesignResultPath!, new JsonObject
            {
                ["id"] = (string?)request["id"],
                ["completed_at"] = "2026-07-26T12:00:00.100Z",
                ["result"] = new JsonObject
                {
                    ["status"] = "previewing",
                    ["changes"] = new JsonArray("Ink: automatic to #ffaa00", "Clock: size 100% to 120%"),
                },
            }.ToJsonString());
        });

        var r = logic.BuildProposeDesk(Args("""{"design":{"name":"  Dusk ","ink":"FA0","layout":{"clock":{"anchor":"bl","size":1.23}}},"note":"Warm and low."}"""));
        await widget;

        Assert.Equal("previewing", (string?)r["status"]);
        Assert.Matches("^[0-9a-f]{32}$", (string?)r["request_id"]);
        Assert.Equal("Ink: automatic to #ffaa00", (string?)r["changes"]![0]);
    }

    [Fact]
    public async Task The_calls_name_and_note_win_over_the_designs()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp, TimeSpan.FromSeconds(5));
        var widget = Task.Run(async () =>
        {
            var design = (JsonObject)((JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.DesignRequestPath!))!)["design"]!;
            Assert.Equal("Outer", (string?)design["name"]);
            Assert.Equal("Outer note.", (string?)design["note"]);
        });
        var r = new ToolLogic(cfg, () => Now).BuildProposeDesk(Args("""{"design":{"name":"Inner","note":"Inner note.","plate":true},"name":"Outer","note":"Outer note."}"""));
        await widget;
        Assert.Equal("queued", (string?)r["status"]);   // nobody answered; the request was what mattered
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("rejected")]
    [InlineData("needs_font")]
    public async Task Each_status_the_widget_may_give_is_passed_on_with_its_details(string status)
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp, TimeSpan.FromSeconds(5));
        var widget = Task.Run(async () =>
        {
            var id = (string?)((JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.DesignRequestPath!))!)["id"];
            File.WriteAllText(cfg.DesignResultPath!, new JsonObject
            {
                ["id"] = id,
                ["result"] = new JsonObject
                {
                    ["status"] = status,
                    ["reasons"] = new JsonArray((JsonNode)new JsonObject { ["field"] = "ink", ["message"] = "no" }),
                    ["fonts"] = new JsonArray((JsonNode)new JsonObject { ["family"] = "Caveat", ["link"] = "https://example.com/c" }),
                    ["private"] = "dropped",
                },
            }.ToJsonString());
        });
        var r = Propose(cfg, """{"plate":true}""");
        await widget;

        Assert.Equal(status, (string?)r["status"]);
        Assert.Equal("ink", (string?)r["reasons"]![0]!["field"]);
        Assert.Equal("Caveat", (string?)r["fonts"]![0]!["family"]);
        Assert.Null(r["private"]);
    }

    [Fact]
    public async Task A_status_the_server_does_not_know_is_not_passed_on_as_success()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp, TimeSpan.FromSeconds(5));
        var widget = Task.Run(async () =>
        {
            var id = (string?)((JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.DesignRequestPath!))!)["id"];
            File.WriteAllText(cfg.DesignResultPath!, new JsonObject { ["id"] = id, ["result"] = new JsonObject { ["status"] = "applied-ish" } }.ToJsonString());
        });
        var r = Propose(cfg, """{"plate":true}""");
        await widget;
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("unexpected_answer", (string?)r["reason"]);
    }

    [Fact]
    public async Task The_phase_one_widget_answers_not_ready_and_the_reason_arrives()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp, TimeSpan.FromSeconds(5));
        var widget = Task.Run(async () =>
        {
            var id = (string?)((JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.DesignRequestPath!))!)["id"];
            File.WriteAllBytes(cfg.DesignResultPath!, DeskDesignHandoffBytes(id!));
        });
        var r = Propose(cfg, """{"plate":true}""");
        await widget;
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("not_ready", (string?)r["reason"]);
        Assert.Equal("design", (string?)r["reasons"]![0]!["field"]);
    }

    /// <summary>What the stub in the App writes (kept literal here: the server must read exactly this shape).</summary>
    private static byte[] DeskDesignHandoffBytes(string id) => System.Text.Encoding.UTF8.GetBytes(new JsonObject
    {
        ["completed_at"] = "2026-07-26T12:00:00.100Z",
        ["id"] = id,
        ["result"] = new JsonObject
        {
            ["status"] = "rejected",
            ["reason"] = "not_ready",
            ["reasons"] = new JsonArray((JsonNode)new JsonObject { ["field"] = "design", ["message"] = "not_ready: this version of Sanduhr checks designs but cannot preview them yet" }),
        },
    }.ToJsonString());

    [Fact]
    public void A_silent_widget_leaves_the_request_queued_and_says_so()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var r = Propose(cfg, """{"plate":true}""");

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.Matches("^[0-9a-f]{32}$", (string?)r["request_id"]);
        Assert.Contains("ten minutes", (string?)r["remedy"]);
        Assert.True(File.Exists(cfg.DesignRequestPath!));
    }

    [Fact]
    public void A_queued_design_that_needs_a_font_still_lists_it()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);   // Segoe UI and Caveat installed

        var r = Propose(cfg, """{"font":{"family":"Pacifico","link":"https://fonts.google.com/specimen/Pacifico"}}""");

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("Pacifico", (string?)r["fonts"]![0]!["family"]);
        Assert.Equal("https://fonts.google.com/specimen/Pacifico", (string?)r["fonts"]![0]!["link"]);
        Assert.True(File.Exists(cfg.DesignRequestPath!));   // valid: the review shows the link and waits
    }

    [Fact]
    public void An_installed_font_is_not_a_need()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);
        Assert.Null(Propose(cfg, """{"font":"Caveat"}""")["fonts"]);
    }

    [Fact]
    public void Theme_warnings_ride_along_in_the_queued_answer()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var theme = ObsidianTheme();
        theme["text_secondary"] = "#0b0b0e";
        var r = new ToolLogic(cfg, () => Now).BuildProposeDesk(new JsonObject { ["design"] = new JsonObject { ["theme"] = theme } });
        Assert.Equal("queued", (string?)r["status"]);
        Assert.NotEmpty(r["warnings"]!.AsArray());
    }

    [Fact]
    public void Another_requests_answer_is_not_taken()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg.DesignResultPath!)!);
        File.WriteAllBytes(cfg.DesignResultPath!, DeskDesignHandoffBytes("someone-elses-id"));
        Assert.Equal("queued", (string?)Propose(cfg, """{"plate":true}""")["status"]);
    }

    [Fact]
    public void A_server_with_no_handoff_paths_is_unavailable_after_the_checks_pass()
    {
        var cfg = new McpConfig
        {
            SnapshotPath = "x", VaultDir = "x", ConsentedRoots = Array.Empty<(string, string)>(), RootsFound = Array.Empty<string>(),
        };
        var ok = new ToolLogic(cfg, () => Now).BuildProposeDesk(Args("""{"design":{"plate":true}}"""));
        Assert.Equal("unavailable", (string?)ok["reason"]);
        var bad = new ToolLogic(cfg, () => Now).BuildProposeDesk(Args("""{"design":{"plate":"yes"}}"""));
        Assert.Equal("rejected", (string?)bad["status"]);
    }

    [Fact]
    public void Without_a_state_file_the_machine_rules_are_left_to_the_widget_but_the_shape_rules_still_hold()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);   // no desk-state.json
        Assert.Equal("queued", (string?)Propose(cfg, """{"font":"Anything","ink":"#fff","launcher":{"buttons":[{"kind":"app","target":"Some.App!x"}]}}""")["status"]);
        File.Delete(cfg.DesignRequestPath!);
        Assert.Equal("rejected", (string?)Propose(cfg, """{"launcher":{"buttons":[{"kind":"app","target":"a b"}]}}""")["status"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void Two_requests_in_a_row_leave_one_request_file_and_no_temp_files()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        Propose(cfg, """{"plate":true}""");
        Propose(cfg, """{"plate":false}""");
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(cfg.DesignRequestPath!)!));
        Assert.False((bool)((JsonObject)JsonNode.Parse(File.ReadAllText(cfg.DesignRequestPath!))!)["design"]!["plate"]!);
    }

    // -- monitors ---------------------------------------------------------------

    [Fact]
    public void Get_passes_the_monitors_on()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteTwoMonitors(cfg);
        var r = new ToolLogic(cfg, () => Now).BuildGetDesk();
        var monitors = r["monitors"]!.AsArray();
        Assert.Equal(2, monitors.Count);
        Assert.Equal(SideId, (string?)monitors[1]!["id"]);
        Assert.Equal("1920\u00D71080, right of main", (string?)monitors[1]!["name"]);
        Assert.Equal(3440, (int)monitors[1]!["left"]!);
    }

    [Fact]
    public void A_piece_on_a_monitor_that_is_connected_is_sent_with_its_monitor()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteTwoMonitors(cfg);
        var r = Propose(cfg, "{\"layout\":{\"clock\":{\"anchor\":\"bl\",\"monitor\":" + JsonValue.Create(SideId)!.ToJsonString() + "},\"claude\":{\"monitor\":\"main\"}}}");
        Assert.Equal("queued", (string?)r["status"]);   // valid: nobody answers, so it is queued
        var request = (JsonObject)JsonNode.Parse(File.ReadAllText(cfg.DesignRequestPath!))!;
        Assert.Equal(SideId, (string?)request["design"]!["layout"]!["clock"]!["monitor"]);
        Assert.Equal("main", (string?)request["design"]!["layout"]!["claude"]!["monitor"]);
    }

    [Fact]
    public void A_monitor_id_the_widget_did_not_list_is_refused_without_a_request()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteTwoMonitors(cfg);
        var r = Propose(cfg, """{"layout":{"clock":{"anchor":"bl","monitor":"\\\\?\\DISPLAY#GONE#1"}}}""");
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("layout.clock.monitor", (string?)r["reasons"]![0]!["field"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void Without_monitors_in_the_state_an_id_is_refused_but_all_and_main_still_pass()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg, s => s["monitors"] = new JsonArray());
        Assert.Equal("rejected", (string?)Propose(cfg, """{"layout":{"clock":{"anchor":"bl","monitor":"abc"}}}""")["status"]);
        Assert.Equal("queued", (string?)Propose(cfg, """{"layout":{"clock":{"anchor":"bl","monitor":"main"}}}""")["status"]);
        File.Delete(cfg.DesignRequestPath!);
        Assert.Equal("queued", (string?)Propose(cfg, """{"layout":{"clock":{"anchor":"bl","monitor":"all"}}}""")["status"]);
    }

    [Fact]
    public void Contrast_is_judged_against_the_monitor_the_piece_is_on()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg, s =>
        {
            var dark = Wallpaper(0.02, 0.02);
            var bright = Wallpaper(0.9, 0.9);
            ((JsonObject)dark["monitors"]![0]!)["id"] = MainId;
            var side = (JsonObject)bright["monitors"]![0]!.DeepClone();
            side["index"] = 1;
            side["primary"] = false;
            side["id"] = SideId;
            dark["monitors"]!.AsArray().Add(side);
            s["wallpaper"] = new JsonObject { ["path"] = @"C:\p.png", ["summary"] = dark };
            s["monitors"] = new JsonArray(
                (JsonNode)new JsonObject { ["id"] = MainId, ["name"] = "main", ["width"] = 2560, ["height"] = 1440, ["primary"] = true, ["left"] = 0, ["top"] = 0 },
                (JsonNode)new JsonObject { ["id"] = SideId, ["name"] = "side", ["width"] = 2560, ["height"] = 1440, ["primary"] = false, ["left"] = 2560, ["top"] = 0 });
        });
        Assert.Equal("queued", (string?)Propose(cfg, """{"ink":"light","layout":{"clock":{"anchor":"bl","monitor":"main"},"claude":{"hidden":true}}}""")["status"]);
        File.Delete(cfg.DesignRequestPath!);
        Assert.Equal("rejected", (string?)Propose(cfg, "{\"ink\":\"light\",\"layout\":{\"clock\":{\"anchor\":\"bl\",\"monitor\":" + JsonValue.Create(SideId)!.ToJsonString() + "},\"claude\":{\"hidden\":true}}}")["status"]);
        AssertNoRequest(cfg);
    }

    [Fact]
    public void The_configs_file_names_are_the_cores()
    {
        var cfg = McpConfig.Resolve(@"C:\h", @"C:\h\.sanduhr", @"C:\h\AppData\Sanduhr", @"C:\h\Local", _ => null);
        Assert.Equal(@"C:\h\.sanduhr\desk-state.json", cfg.DesignStatePath);
        Assert.Equal(@"C:\h\.sanduhr\desk-design-request.json", cfg.DesignRequestPath);
        Assert.Equal(@"C:\h\.sanduhr\desk-design-result.json", cfg.DesignResultPath);
    }
}
