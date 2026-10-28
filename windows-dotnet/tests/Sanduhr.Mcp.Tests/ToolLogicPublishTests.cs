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
        VaultDir = Path.Combine(tmp.Path, "vault"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        McpSettingsPath = Path.Combine(tmp.Path, "mcp-settings.json"),
        PublishRequestPath = Path.Combine(tmp.Path, "publish-request.json"),
        PublishResultPath = Path.Combine(tmp.Path, "publish-result.json"),
        PublishWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        PublishPollInterval = TimeSpan.FromMilliseconds(20),
    };

    /// <summary>Write the widget's mcp-settings.json projection (schema 1).</summary>
    private static void WriteSettings(TempDir tmp, bool? enabled, bool? credentialReady,
        bool? hasEndpoint = true, int schema = 1)
    {
        var group = new JsonObject();
        if (enabled is { } e) group["enabled"] = e;
        if (hasEndpoint is { } h) group["has_endpoint"] = h;
        if (credentialReady is { } k) group["credential_ready"] = k;
        File.WriteAllText(Path.Combine(tmp.Path, "mcp-settings.json"),
            new JsonObject
            {
                ["schema_version"] = schema,
                ["mcp_roots"] = new JsonObject(),
                ["publish"] = group,
            }.ToJsonString());
    }

    [Fact]
    public void Handoff_file_names_mirror_core_publish_handoff()
    {
        using var tmp = new TempDir();
        string home = Path.Combine(tmp.Path, "home");
        var cfg = McpConfig.Resolve(home, Path.Combine(home, ".sanduhr"), Path.Combine(tmp.Path, "appdata", "Sanduhr"),
            Path.Combine(tmp.Path, "localappdata"), _ => null);
        Assert.EndsWith("publish-request.json", cfg.PublishRequestPath);
        Assert.EndsWith("publish-result.json", cfg.PublishResultPath);
        Assert.EndsWith("mcp-settings.json", cfg.McpSettingsPath);
        Assert.Contains(".sanduhr", cfg.PublishRequestPath);
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
        WriteSettings(tmp, enabled: true, credentialReady: true, hasEndpoint: false);
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
        WriteSettings(tmp, enabled: true, credentialReady: true, hasEndpoint: null);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("no_endpoint", (string?)r["status"]);
    }

    [Fact]
    public void Enabled_with_endpoint_but_no_token_is_no_token_with_remedy()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, credentialReady: false);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("no_token", (string?)r["status"]);
        Assert.Equal("no_publish_token", (string?)r["reason"]);
        Assert.Contains("publish token", (string?)r["remedy"]);
        Assert.Contains("Set token", (string?)r["remedy"]);
        Assert.DoesNotContain("626", (string?)r["remedy"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "publish-request.json")));
    }

    [Fact]
    public void Ready_projection_queues()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, credentialReady: true);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("queued", (string?)r["status"]);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "publish-request.json")));
    }

    [Fact]
    public void Refusal_order_is_disabled_then_endpoint_then_token()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: false, credentialReady: false, hasEndpoint: false);
        Assert.Equal("disabled", (string?)new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null)["status"]);
        WriteSettings(tmp, enabled: true, credentialReady: false, hasEndpoint: false);
        Assert.Equal("no_endpoint", (string?)new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null)["status"]);
    }

    [Fact]
    public void Other_schema_version_fails_closed_as_disabled()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, credentialReady: true, schema: 2);
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("disabled", (string?)r["status"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "publish-request.json")));
    }

    [Fact]
    public void Projection_without_a_publish_group_is_disabled()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "mcp-settings.json"), """{"schema_version":1,"mcp_roots":{}}""");
        Assert.Equal("disabled", (string?)new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null)["status"]);
    }

    [Fact]
    public void Settings_json_beside_the_projection_is_not_consulted()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"),
            """{"publish":{"enabled":true,"token_stored":true,"endpoint_url":"https://x.example"}}""");
        Assert.Equal("disabled", (string?)new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null)["status"]);
    }

    [Fact]
    public void Queues_a_request_for_yesterday_by_default_and_reports_queued_when_the_widget_is_silent()
    {
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, credentialReady: true);
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
        WriteSettings(tmp, enabled: true, credentialReady: true);
        var cfg = PublishConfig(tmp, wait: Waits.AnswerWait);
        string reqPath = cfg.PublishRequestPath!;
        string resPath = cfg.PublishResultPath!;

        // Play the widget: wait for the request, answer it, clear it.
        // The server writes the request in place, so the file can exist before
        // its writer has closed it (an open fails with a sharing violation) or
        // before its JSON is complete. Wait for a whole, readable request, the
        // same condition the real widget waits on, instead of the file's mere
        // existence.
        var widget = Task.Run(async () =>
        {
            JsonObject? req;
            while ((req = TryReadRequest(reqPath)) is null)
                await Task.Delay(10);
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

    /// <summary>The request once its writer is done: null while the file is
    /// missing, still open for writing, or not yet a complete JSON object.</summary>
    private static JsonObject? TryReadRequest(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return JsonNode.Parse(sr.ReadToEnd()) as JsonObject;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    [Fact]
    public void Unreadable_settings_fail_closed_as_disabled()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "mcp-settings.json"), "{ nope");
        var r = new ToolLogic(PublishConfig(tmp), () => Now).BuildPublish(null);
        Assert.Equal("disabled", (string?)r["status"]);
    }

    [Fact]
    public void Request_is_replaced_atomically_not_rewritten_in_place()
    {
        // The widget may be reading the request while a new one is queued. An in-place
        // rewrite truncates under the reader (a torn, unparseable file); a temp sibling
        // plus replace leaves the reader's handle on the old bytes.
        using var tmp = new TempDir();
        WriteSettings(tmp, enabled: true, credentialReady: true);
        var cfg = PublishConfig(tmp);
        string path = cfg.PublishRequestPath!;
        byte[] oldBytes = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"old\",\"date\":\"2026-01-01\",\"requested_at\":\"2026-01-02T00:00:00Z\"}");
        File.WriteAllBytes(path, oldBytes);

        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var r = new ToolLogic(cfg, () => Now).BuildPublish(null);
        Assert.Equal("queued", (string?)r["status"]);

        var buf = new byte[oldBytes.Length + 512];
        int n = held.Read(buf, 0, buf.Length);
        Assert.Equal(oldBytes, buf.AsSpan(0, n).ToArray());

        var current = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Assert.NotNull(current);
        Assert.NotEqual("old", (string?)current!["id"]);
        Assert.Equal((string?)r["request_id"], (string?)current["id"]);
        Assert.False(File.Exists(path + ".tmp"), "the temp sibling must not linger");
    }
}
