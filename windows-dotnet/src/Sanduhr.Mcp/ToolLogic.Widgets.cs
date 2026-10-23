using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

// propose_widget (3.8, widgets made with Claude). The pack is built and checked with Core's own rules (WidgetProposal and the
// PackValidator it runs, linked here), so the server refuses exactly what the widget would, before anything is written.
// It never installs anything: it queues widget-request.json and waits for the widget's answer in widget-result.json
// (previewing, applied, rejected), or answers queued when the widget does not. A new pack always waits for the user's Apply:
// "Let Claude change the Desk directly" does not cover widgets. A refused pack leaves no request file.
public sealed partial class ToolLogic
{
    public JsonObject BuildProposeWidget(JsonObject? args)
    {
        var build = WidgetProposal.Build(args, _clock());
        if (build.Pack is not { } pack) return RejectedWidget(build.Reasons);

        if (_config.WidgetRequestPath is null || _config.WidgetResultPath is null)
            return Refusal("no_data", "unavailable", "The widget tools are not wired on this server configuration.");

        string? note = args?["note"] is JsonValue nv && nv.TryGetValue<string>(out var n) ? n : null;
        string id = Guid.NewGuid().ToString("N");
        string json = WidgetProposal.RequestJson(id, pack, note, _clock()).ToJsonString();
        if (json.Length > WidgetProposal.MaxRequestBytes)
            return RejectedWidget([new DeskReason("pack", "is too large for the widget to read (a Claude-made pack is at most 4 MB of JSON)")]);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_config.WidgetRequestPath)!);
            WriteRequestAtomically(_config.WidgetRequestPath, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refusal("error", "request_write_failed", $"Could not queue the request ({e.GetType().Name}).");
        }

        var deadline = DateTimeOffset.UtcNow + _config.WidgetWaitTimeout;
        while (true)
        {
            if (TryReadResult(_config.WidgetResultPath, id) is { } result)
                return WidgetAnswerJson(result, pack.Id, id);
            if (DateTimeOffset.UtcNow >= deadline) break;
            Thread.Sleep(_config.WidgetPollInterval);
        }

        // The request stays for the widget's next look within its ten-minute window.
        return new JsonObject
        {
            ["status"] = "queued",
            ["reason"] = "widget_not_responding",
            ["pack_id"] = pack.Id,
            ["request_id"] = id,
            ["remedy"] = $"The Sanduhr widget did not answer within {(int)_config.WidgetWaitTimeout.TotalSeconds} seconds. "
                         + "Start it: it picks the request up within ten minutes of when it was made and previews the widget for the user to Apply or Dismiss.",
        };
    }

    /// <summary>The widget's answer, passed on: its status (one of the three it may give), reasons, the pack id and whether
    /// the user made a sprite the companion.</summary>
    private static JsonObject WidgetAnswerJson(JsonObject result, string packId, string id)
    {
        string status = result["status"] is JsonValue sv && sv.TryGetValue<string>(out var word) && IsWidgetStatus(word)
            ? word
            : "rejected";
        var answer = new JsonObject { ["status"] = status };
        foreach (var key in new[] { "reason", "reasons", "companion" })
            if (result[key] is { } value) answer[key] = value.DeepClone();
        if (status == "rejected" && answer["reasons"] is null && answer["reason"] is null)
            answer["reason"] = "unexpected_answer";
        answer["pack_id"] = packId;
        answer["request_id"] = id;
        return answer;
    }

    private static bool IsWidgetStatus(string word) => WidgetAnswer.Statuses.Contains(word, StringComparer.Ordinal);

    private static JsonObject RejectedWidget(IReadOnlyList<DeskReason> reasons)
    {
        var list = new JsonArray();
        foreach (var r in reasons) list.Add((JsonNode?)r.ToJson());
        return new JsonObject { ["status"] = "rejected", ["reasons"] = list };
    }
}
