using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

public class McpServerTests
{
    /// <summary>Run the server loop over scripted stdin lines, return responses.</summary>
    private static List<JsonObject> Run(TempDir tmp, params string[] lines)
    {
        var logic = new ToolLogic(Config(Path.Combine(tmp.Path, "snapshot.json")), () => Now);
        var output = new StringWriter();
        new McpServer(new StringReader(string.Join('\n', lines)), output, logic).Run();
        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => (JsonObject)JsonNode.Parse(l)!)
            .ToList();
    }

    private static string Req(int id, string method, string? paramsJson = null)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\"{(paramsJson is null ? "" : $",\"params\":{paramsJson}")}}}";

    [Fact]
    public void Initialize_echoes_protocol_version_and_names_the_server()
    {
        using var tmp = new TempDir();
        var res = Run(tmp, Req(1, "initialize", """{"protocolVersion":"2025-03-26","capabilities":{}}"""));
        var result = (JsonObject)res.Single()["result"]!;
        Assert.Equal("2025-03-26", (string?)result["protocolVersion"]);
        Assert.Equal("sanduhr", (string?)result["serverInfo"]!["name"]);
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public void Tools_list_has_exactly_three_read_only_tools()
    {
        using var tmp = new TempDir();
        var res = Run(tmp, Req(1, "tools/list"));
        var tools = (JsonArray)((JsonObject)res.Single()["result"]!)["tools"]!;
        Assert.Equal(new[] { "get_usage", "get_local_burn_by_project", "ping" },
            tools.Select(t => (string?)t!["name"]).ToArray());
        Assert.All(tools, t => Assert.True(t!["annotations"]!["readOnlyHint"]!.GetValue<bool>()));
        // The behavioral trigger IS the feature (review: without it, never fires).
        Assert.Contains("Call BEFORE", (string?)tools[0]!["description"]);
        Assert.Contains("never assume budget", (string?)tools[0]!["description"]);
    }

    [Fact]
    public void Burn_tool_params_are_a_closed_enum_no_free_form_paths()
    {
        using var tmp = new TempDir();
        var res = Run(tmp, Req(1, "tools/list"));
        var tools = (JsonArray)((JsonObject)res.Single()["result"]!)["tools"]!;
        var schema = (JsonObject)tools[1]!["inputSchema"]!;
        var props = (JsonObject)schema["properties"]!;
        Assert.Equal(new[] { "window_days", "full_paths" }, props.Select(p => p.Key).ToArray());
        Assert.Equal(new[] { 1, 7, 30 },
            ((JsonArray)props["window_days"]!["enum"]!).Select(n => n!.GetValue<int>()).ToArray());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void Tool_call_wraps_the_typed_payload_and_never_sets_isError()
    {
        using var tmp = new TempDir();
        // No snapshot on disk: the payload is a typed no_data — still isError:false.
        var res = Run(tmp, Req(1, "tools/call", """{"name":"get_usage","arguments":{}}"""));
        var result = (JsonObject)res.Single()["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var payload = (JsonObject)JsonNode.Parse((string)result["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.Equal("no_data", (string?)payload["status"]);
        Assert.Equal("missing", (string?)payload["reason"]);
    }

    [Fact]
    public void Protocol_ping_and_tool_ping_both_answer()
    {
        using var tmp = new TempDir();
        var res = Run(tmp,
            Req(1, "ping"),
            Req(2, "tools/call", """{"name":"ping","arguments":{}}"""));
        Assert.Equal(2, res.Count);
        Assert.NotNull(res[0]["result"]);
        var payload = (JsonObject)JsonNode.Parse((string)res[1]["result"]!["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.False(payload["snapshot_found"]!.GetValue<bool>());
    }

    [Fact]
    public void Unknown_method_and_unknown_tool_are_protocol_errors()
    {
        using var tmp = new TempDir();
        var res = Run(tmp,
            Req(1, "resources/list"),
            Req(2, "tools/call", """{"name":"switch_account","arguments":{}}"""));
        Assert.Equal(-32601, res[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, res[1]["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void Notifications_are_never_answered()
    {
        using var tmp = new TempDir();
        var res = Run(tmp,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            Req(1, "ping"));
        Assert.Single(res); // only the ping reply
    }

    [Fact]
    public void Garbage_and_blank_lines_do_not_kill_the_loop()
    {
        using var tmp = new TempDir();
        var res = Run(tmp, "not json at all", "", "   ", Req(1, "ping"));
        Assert.Equal(2, res.Count);
        Assert.Equal(-32700, res[0]["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(res[1]["result"]);
    }

    [Fact]
    public void Output_frames_are_pure_ascii()
    {
        using var tmp = new TempDir();
        // Plan name carries a non-ASCII glyph in the wild ("Max ×20") — the wire
        // must stay ASCII regardless (the statusline mojibake lesson).
        File.WriteAllText(Path.Combine(tmp.Path, "snapshot.json"),
            OkSnapshotJson(Now).Replace("Max 20x", "Max ×20"));
        var logic = new ToolLogic(Config(Path.Combine(tmp.Path, "snapshot.json")), () => Now);
        var output = new StringWriter();
        new McpServer(new StringReader(Req(1, "tools/call", """{"name":"get_usage","arguments":{}}""")), output, logic).Run();
        Assert.DoesNotMatch("[^\\u0000-\\u007F]", output.ToString());
        Assert.Contains("\\u00D7", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
