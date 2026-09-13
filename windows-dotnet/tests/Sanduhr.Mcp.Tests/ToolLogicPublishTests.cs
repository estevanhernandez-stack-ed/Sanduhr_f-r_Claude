using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>publish_usage is a handoff, not an upload: this server queues a
/// request file and returns the widget's typed result. Every refusal is a
/// typed result (never a protocol error), and the server never needs a key.</summary>
public class ToolLogicPublishTests
{
    private static McpConfig PublishConfig(TempDir tmp, TimeSpan? wait = null) => new()
    {
        SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        SettingsPath = Path.Combine(tmp.Path, "settings.json"),
        PublishRequestPath = Path.Combine(tmp.Path, "publish-request.json"),
        PublishResultPath = Path.Combine(tmp.Path, "publish-result.json"),
        PublishWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        PublishPollInterval = TimeSpan.FromMilliseconds(20),
    };

    private static void WriteSettings(TempDir tmp, bool? enabled, bool? keyStored)
    {
        var group = new JsonObject();
        if (enabled is { } e) group["enabled"] = e;
        if (keyStored is { } k) group["key_stored"] = k;
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"),
            new JsonObject { ["theme"] = "obsidian", ["publish_626"] = group }.ToJsonString());
    }

    [Fact]
    public void Handoff_file_names_mirror_core_publish_handoff()
    {
        var cfg = McpConfig.Resolve();
        Assert.EndsWith("publish-request.json", cfg.PublishRequestPath);
        Assert.EndsWith("publish-result.json", cfg.PublishResultPath);
        Assert.EndsWith("settings.json", cfg.SettingsPath);
    }

    [Fact]
    public void Invalid_date_is_a_typed_result_not_a_protocol_error()
    {
        using var tmp = new TempDir();
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish("13/09/2026");
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("invalid_params", (string?)r["reason"]);
    }

    [Fact]
    public void Missing_settings_is_disabled_with_remedy()
    {
        using var tmp = new TempDir();
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("disabled", (string?)r["status"]);
        Assert.Equal("publishing_off", (string?)r["reason"]);
        Assert.Contains("Publish to 626 Labs", (string?)r["remedy"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "publish-request.json")), "a refusal must not queue anything");
    }

    [Fact]
    public void Enabled_without_key_is_no_key_with_remedy()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, keyStored: false);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("no_key", (string?)r["status"]);
        Assert.Equal("no_agent_key", (string?)r["reason"]);
        Assert.Contains("agent key", (string?)r["remedy"]);
    }

    [Fact]
    public void Queues_a_request_for_yesterday_by_default_and_reports_queued_when_the_widget_is_silent()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, keyStored: true);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.Contains("start", (string?)r["remedy"], StringComparison.OrdinalIgnoreCase);
        // Now = 2026-07-26T12:00Z; yesterday in any local zone within +-12h is 07-25.
        Assert.Equal("2026-07-25", (string?)r["date"]);

        var req = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(tmp.Path, "publish-request.json")))!;
        Assert.Equal((string?)r["request_id"], (string?)req["id"]);
        Assert.Equal("2026-07-25", (string?)req["date"]);
        Assert.NotNull(req["requested_at"]);
    }

    [Fact]
    public async Task Returns_the_widgets_result_when_it_answers_the_matching_request()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, keyStored: true);
        var cfg = PublishConfig(tmp, wait: TimeSpan.FromSeconds(5));
        string reqPath = cfg.PublishRequestPath!;
        string resPath = cfg.PublishResultPath!;

        // Play the widget: wait for the request, answer it, clear it.
        var widget = Task.Run(async () =>
        {
            while (!File.Exists(reqPath))
                await Task.Delay(10);
            var req = (JsonObject)JsonNode.Parse(File.ReadAllText(reqPath))!;
            // A stale result for a DIFFERENT id must be ignored by the poller.
            File.WriteAllText(resPath, new JsonObject { ["id"] = "old", ["result"] = new JsonObject { ["status"] = "error" } }.ToJsonString());
            await Task.Delay(30);
            File.WriteAllText(resPath, new JsonObject
            {
                ["id"] = (string?)req["id"],
                ["result"] = new JsonObject { ["status"] = "ok", ["http_status"] = 200, ["message"] = "Published." },
            }.ToJsonString());
            File.Delete(reqPath);
        });

        var r = await Task.Run(() => new ToolLogic(cfg, () => Now).BuildPublish("2026-07-20"));
        await widget;

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(200, r["http_status"]!.GetValue<int>());
        Assert.Equal("2026-07-20", (string?)r["date"]);
        Assert.False(File.Exists(reqPath));
    }

    [Fact]
    public void Unreadable_settings_fail_closed_as_disabled()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), "{ nope");
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("disabled", (string?)r["status"]);
    }
}
