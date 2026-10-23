using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>propose_widget (3.8): the pack built and validated before anything is written, the request handoff, and the widget's
/// answer passed on. Temp folders only.</summary>
public class ToolLogicWidgetTests
{
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

    private static McpConfig WidgetConfig(TempDir tmp, TimeSpan? wait = null) => new()
    {
        SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
        VaultDir = Path.Combine(tmp.Path, "vault"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        WidgetRequestPath = Path.Combine(tmp.Path, "ext", WidgetProposal.RequestFile),
        WidgetResultPath = Path.Combine(tmp.Path, "ext", WidgetProposal.ResultFile),
        WidgetWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        WidgetPollInterval = TimeSpan.FromMilliseconds(20),
    };

    private const string Frame =
        """["..0000..",".011110.","01122110","01122110","01111110",".011110.","..0..0..",".00..00."]""";

    private static JsonObject SpriteArgs(Action<JsonObject>? edit = null)
    {
        var args = (JsonObject)JsonNode.Parse($$"""
        {
          "kind": "sprite",
          "pack": { "id": "blob", "name": "Blob", "description": "A small blob." },
          "sprite": {
            "schema_version": 2, "frame_w": 8, "frame_h": 8, "scale": 2, "fps": 8, "sheet": "sheet.json",
            "animations": { "walk": [0, 1], "sit": [0], "nap": [1], "pop": [0], "cheer": [1], "worry": [0], "focus": [1], "sleep": [0], "celebrate": [1] },
            "moods": { "content": "sit", "focused": "focus", "worried": "worry", "exhausted": "sleep", "celebrate": "celebrate" },
            "triggers": { "needs_you": "pop", "job_passed": "cheer", "near_limit": "worry", "idle": "nap" },
            "roam": { "edges": ["bottom"], "speed_px_s": 20 }
          },
          "sheets": { "sheet.json": { "palette": ["#22d3ee", "#a855f7", "#ec4899"], "frame_w": 8, "frame_h": 8, "frames": [{{Frame}}, {{Frame}}] } },
          "note": "A small friend."
        }
        """)!;
        edit?.Invoke(args);
        return args;
    }

    private static string Reasons(JsonObject r) => string.Join(" | ", (r["reasons"] as JsonArray ?? []).Select(x => (string?)x!["message"]));

    [Fact]
    public void A_pack_that_breaks_a_rule_is_rejected_with_reasons_and_nothing_is_written()
    {
        using var tmp = new TempDir();
        var cfg = WidgetConfig(tmp);
        var logic = new ToolLogic(cfg, () => Now);
        foreach (var (args, needle) in new (JsonObject, string)[]
        {
            (SpriteArgs(a => a["pack"]!["name"] = "Claude blob"), "trademark"),
            (SpriteArgs(a => a["sprite"]!["sheet"] = "sprite.png"), "never supplies an image"),
            (SpriteArgs(a => a["sheets"]!["sheet.json"]!["palette"]![0] = "#ffffff"), "pure white"),
            (SpriteArgs(a => a["kind"] = "theme"), "propose_theme"),
            (SpriteArgs(a => a["kind"] = "font"), "fonts are binaries"),
            (SpriteArgs(a => a["png"] = "AAAA"), "unknown argument"),
        })
        {
            var r = logic.BuildProposeWidget(args);
            Assert.Equal("rejected", (string?)r["status"]);
            Assert.Contains(needle, Reasons(r));
        }
        Assert.False(File.Exists(cfg.WidgetRequestPath));
    }

    [Fact]
    public async Task A_good_pack_is_queued_namespaced_and_the_widgets_answer_comes_back()
    {
        using var tmp = new TempDir();
        var cfg = WidgetConfig(tmp, TimeSpan.FromSeconds(5));
        var logic = new ToolLogic(cfg, () => Now);

        var widget = Task.Run(async () =>
        {
            var request = (JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.WidgetRequestPath!))!;
            Assert.Equal(1, (int)request["schema_version"]!);
            Assert.Equal("my-blob", (string?)request["pack_id"]);
            Assert.Equal("A small friend.", (string?)request["note"]);
            var files = (JsonObject)request["files"]!;
            Assert.Equal(["LICENSE", "pack.json", "sheet.json", "sprite.json"], files.Select(f => f.Key).Order(StringComparer.Ordinal));
            Assert.All(files, f => Assert.IsAssignableFrom<JsonValue>(f.Value));   // text only: data, never bytes
            File.Delete(cfg.WidgetRequestPath!);
            File.WriteAllText(cfg.WidgetResultPath!, new JsonObject
            {
                ["id"] = (string?)request["id"],
                ["completed_at"] = "2026-07-26T12:00:00.100Z",
                ["result"] = new JsonObject { ["status"] = "previewing", ["pack_id"] = "my-blob" },
            }.ToJsonString());
        });

        var r = logic.BuildProposeWidget(SpriteArgs());
        await widget;
        Assert.Equal("previewing", (string?)r["status"]);
        Assert.Equal("my-blob", (string?)r["pack_id"]);
        Assert.Matches("^[0-9a-f]{32}$", (string?)r["request_id"]);
    }

    [Fact]
    public async Task An_answer_outside_the_widgets_statuses_is_passed_on_as_rejected()
    {
        using var tmp = new TempDir();
        var cfg = WidgetConfig(tmp, TimeSpan.FromSeconds(5));
        var logic = new ToolLogic(cfg, () => Now);
        var widget = Task.Run(async () =>
        {
            var request = (JsonObject)JsonNode.Parse(await ReadWhenReady(cfg.WidgetRequestPath!))!;
            File.WriteAllText(cfg.WidgetResultPath!, new JsonObject
            {
                ["id"] = (string?)request["id"],
                ["result"] = new JsonObject { ["status"] = "installed_without_asking" },
            }.ToJsonString());
        });
        var r = logic.BuildProposeWidget(SpriteArgs());
        await widget;
        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("unexpected_answer", (string?)r["reason"]);
    }

    [Fact]
    public void With_no_widget_answering_the_request_waits_as_queued()
    {
        using var tmp = new TempDir();
        var cfg = WidgetConfig(tmp);
        var r = new ToolLogic(cfg, () => Now).BuildProposeWidget(SpriteArgs());
        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.Equal("my-blob", (string?)r["pack_id"]);
        Assert.True(File.Exists(cfg.WidgetRequestPath));
    }

    [Fact]
    public void Without_the_handoff_paths_the_tool_says_it_is_unavailable()
    {
        using var tmp = new TempDir();
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = Path.Combine(tmp.Path, "vault"),
            ConsentedRoots = Array.Empty<(string, string)>(),
            RootsFound = Array.Empty<string>(),
        };
        var r = new ToolLogic(cfg, () => Now).BuildProposeWidget(SpriteArgs());
        Assert.Equal("unavailable", (string?)r["reason"]);
    }

    [Fact]
    public void Resolve_points_the_handoff_at_the_external_folder()
    {
        using var tmp = new TempDir();
        var cfg = McpConfig.Resolve(tmp.Path, Path.Combine(tmp.Path, ".sanduhr"), Path.Combine(tmp.Path, "AppData"), Path.Combine(tmp.Path, "Local"), _ => null);
        Assert.Equal(Path.Combine(tmp.Path, ".sanduhr", "widget-request.json"), cfg.WidgetRequestPath);
        Assert.Equal(Path.Combine(tmp.Path, ".sanduhr", "widget-result.json"), cfg.WidgetResultPath);
    }
}
