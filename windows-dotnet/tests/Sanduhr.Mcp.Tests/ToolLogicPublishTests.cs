using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>publish_usage is a handoff, not an upload: this server queues a
/// request file and returns the widget's typed result. Every refusal is a
/// typed result (never a protocol error), the server never needs a token,
/// and the refusal vocabulary names the publish endpoint, not a vendor.</summary>
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

    private static void WriteSettings(TempDir tmp, bool? enabled, bool? tokenStored,
        string? endpointUrl = "https://collector.example/usage", string? authScheme = null)
    {
        var group = new JsonObject();
        if (enabled is { } e) group["enabled"] = e;
        if (tokenStored is { } k) group["token_stored"] = k;
        if (endpointUrl is { } u) group["endpoint_url"] = u;
        if (authScheme is { } a) group["auth_scheme"] = a;
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"),
            new JsonObject { ["theme"] = "obsidian", ["publish"] = group }.ToJsonString());
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
        Assert.Contains("Publish usage", (string?)r["remedy"]);
        Assert.DoesNotContain("626", (string?)r["remedy"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "publish-request.json")), "a refusal must not queue anything");
    }

    [Fact]
    public void Enabled_without_an_endpoint_is_no_endpoint_with_remedy()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, tokenStored: true, endpointUrl: "");
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("no_endpoint", (string?)r["status"]);
        Assert.Equal("no_endpoint_url", (string?)r["reason"]);
        Assert.Contains("publish endpoint", (string?)r["remedy"]);
        Assert.Contains("Endpoint URL", (string?)r["remedy"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "publish-request.json")));
    }

    [Fact]
    public void Endpoint_absent_from_the_group_is_no_endpoint_too()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, tokenStored: true, endpointUrl: null);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("no_endpoint", (string?)r["status"]);
    }

    [Fact]
    public void Enabled_with_endpoint_but_no_token_is_no_token_with_remedy()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, tokenStored: false);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("no_token", (string?)r["status"]);
        Assert.Equal("no_publish_token", (string?)r["reason"]);
        Assert.Contains("publish token", (string?)r["remedy"]);
        Assert.Contains("Set token", (string?)r["remedy"]);
        Assert.DoesNotContain("626", (string?)r["remedy"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "publish-request.json")));
    }

    [Fact]
    public void Auth_scheme_none_needs_no_token_and_queues()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, tokenStored: false, authScheme: "none");
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("queued", (string?)r["status"]);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "publish-request.json")));
    }

    [Fact]
    public void Refusal_order_is_disabled_then_endpoint_then_token()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: false, tokenStored: false, endpointUrl: "");
        Assert.Equal("disabled", (string?)new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null)["status"]);
        WriteSettings(tmp, enabled: true, tokenStored: false, endpointUrl: "");
        Assert.Equal("no_endpoint", (string?)new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null)["status"]);
    }

    [Fact]
    public void A_not_yet_migrated_publish_626_group_is_honored_read_only()
    {
        using var tmp = new TempDir();
        var legacy = new JsonObject { ["enabled"] = true, ["key_stored"] = false };
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"),
            new JsonObject { ["publish_626"] = legacy }.ToJsonString());

        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);

        // The legacy group described the 626 Labs endpoint, so it counts as set; the token does not.
        Assert.Equal("no_token", (string?)r["status"]);
        // Read-only: the server never rewrites settings.json.
        var after = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(tmp.Path, "settings.json")))!;
        Assert.NotNull(after["publish_626"]);
        Assert.Null(after["publish"]);
    }

    [Fact]
    public void Queues_a_request_for_yesterday_by_default_and_reports_queued_when_the_widget_is_silent()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, tokenStored: true);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.Contains("start", (string?)r["remedy"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Publish usage", (string?)r["remedy"]);
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
        WriteSettings(tmp, enabled: true, tokenStored: true);
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
