using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>
/// Minimal MCP server over newline-delimited JSON-RPC 2.0 on stdio — the whole
/// surface a tools-only stdio server needs: initialize, notifications/initialized,
/// ping, tools/list, tools/call. Hand-rolled deliberately (design review §packaging):
/// zero protocol dependencies keeps the cert-letter claim minimal ("on-demand
/// stdio child, no service, no network listener, no third-party protocol stack")
/// and single-file publish trivial.
///
/// Tool failures are TYPED RESULTS (status/reason/remedy in the payload, and
/// isError stays false) — JSON-RPC errors are reserved for protocol-level
/// breakage (unknown method/tool, unparseable frame).
/// </summary>
public sealed class McpServer
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly TextReader _in;
    private readonly TextWriter _out;
    private readonly ToolLogic _tools;

    public McpServer(TextReader input, TextWriter output, ToolLogic tools)
    {
        _in = input;
        _out = output;
        _tools = tools;
    }

    public void Run()
    {
        string? line;
        while ((line = _in.ReadLine()) is not null)
        {
            if (line.Trim().Length == 0)
                continue;
            JsonObject? msg;
            try
            {
                msg = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                WriteError(null, -32700, "parse error");
                continue;
            }
            if (msg is null)
            {
                WriteError(null, -32700, "parse error");
                continue;
            }
            Dispatch(msg);
        }
    }

    private void Dispatch(JsonObject msg)
    {
        string? method = (string?)msg["method"];
        JsonNode? id = msg["id"];
        bool isNotification = id is null;

        switch (method)
        {
            case "initialize":
                WriteResult(id, BuildInitializeResult(msg["params"] as JsonObject));
                break;
            case "notifications/initialized":
            case "notifications/cancelled":
                break; // fire-and-forget by contract
            case "ping":
                WriteResult(id, new JsonObject());
                break;
            case "tools/list":
                WriteResult(id, new JsonObject { ["tools"] = ToolCatalog.Build() });
                break;
            case "tools/call":
                HandleToolCall(id, msg["params"] as JsonObject);
                break;
            default:
                if (!isNotification)
                    WriteError(id, -32601, $"method not found: {method}");
                break;
        }
    }

    private JsonObject BuildInitializeResult(JsonObject? p)
    {
        // Echo the client's protocol version when it names one (the negotiation
        // contract: a server that supports the requested version echoes it).
        string requested = (string?)p?["protocolVersion"] is { Length: > 0 } v ? v : ProtocolVersion;
        return new JsonObject
        {
            ["protocolVersion"] = requested,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "sanduhr",
                ["title"] = "Sanduhr - Claude subscription usage",
                ["version"] = ToolLogic.ServerVersion,
            },
        };
    }

    private void HandleToolCall(JsonNode? id, JsonObject? p)
    {
        string? name = (string?)p?["name"];
        var args = p?["arguments"] as JsonObject;
        JsonObject payload;
        switch (name)
        {
            case "get_usage":
                payload = _tools.BuildUsage();
                break;
            case "get_local_burn_by_project":
                int windowDays = 7;
                bool fullPaths = false;
                try { if (args?["window_days"] is JsonNode w) windowDays = w.GetValue<int>(); }
                catch { windowDays = -1; } // non-integer -> typed invalid_params from BuildBurn
                try { if (args?["full_paths"] is JsonNode f) fullPaths = f.GetValue<bool>(); }
                catch { /* non-bool stays default false */ }
                payload = _tools.BuildBurn(windowDays, fullPaths);
                break;
            case "get_model_usage":
                int muWindow = 7;
                try { if (args?["window_days"] is JsonNode mw) muWindow = mw.GetValue<int>(); }
                catch { muWindow = -1; } // non-integer -> typed invalid_params
                payload = _tools.BuildModelUsage(muWindow);
                break;
            case "get_usage_history":
                int histWindow = 30;
                try { if (args?["window_days"] is JsonNode hw) histWindow = hw.GetValue<int>(); }
                catch { histWindow = -1; }
                payload = _tools.BuildHistory(histWindow);
                break;
            case "ping":
                payload = _tools.BuildPing();
                break;
            default:
                WriteError(id, -32602, $"unknown tool: {name}");
                return;
        }
        WriteResult(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
                },
            },
            ["isError"] = false,
        });
    }

    private void WriteResult(JsonNode? id, JsonObject result)
    {
        if (id is null)
            return; // never answer a notification
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        });
    }

    private void WriteError(JsonNode? id, int code, string message)
    {
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        });
    }

    private void Write(JsonObject frame)
    {
        // Default STJ escaping emits pure-ASCII output — encoding-proof on any
        // console codepage (the statusline smoke's mojibake lesson, applied here).
        _out.WriteLine(frame.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        _out.Flush();
    }
}
