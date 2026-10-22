using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

// propose_now_playing_looks, a look per song (3.7.0). Ported from the Mac's sanduhr_mcp.py (item 65c). The checks are Core's own
// (NowPlayingLookCheck and NowPlayingLookProposal, linked here), so the server refuses exactly what the widget would, including
// the contrast rule against the wallpaper under the now-playing piece when desk-state.json lets it judge. The tool never saves a
// look: it queues now-playing-looks-request.json, the widget checks it again and either shows the suggestion for the user to Save
// or Dismiss or, when the user lets Claude style songs directly, saves it, and answers in now-playing-looks-result.json. A refused
// request leaves no file. This server never reads what is playing and never reads the saved looks.
public sealed partial class ToolLogic
{
    private static readonly string[] LooksArgs = ["looks", "note"];

    private static readonly string[] LooksAnswerStatuses = ["pending_approval", "applied", "rejected"];

    public JsonObject BuildProposeNowPlayingLooks(JsonObject? args)
    {
        args ??= new JsonObject();
        var reasons = new List<string>();
        var unknown = args.Select(kv => kv.Key).Where(k => !LooksArgs.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            reasons.Add("unknown argument(s): " + string.Join(", ", unknown.Take(5).Select(k => Clip(k, 20))));

        string? note = null;
        if (args["note"] is { } n)
        {
            if (!TryString(n, out var raw)) reasons.Add("note must be a string");
            else if (raw.EnumerateRunes().Count() > NowPlayingLookCheck.MaxNote || raw.EnumerateRunes().Any(MessageProposal.IsControl))
                reasons.Add($"note must be one line of at most {NowPlayingLookCheck.MaxNote} characters");
            else if (raw.Trim().Length > 0) note = raw.Trim();
        }

        // What the wallpaper allows is judged here when desk-state.json says; the widget, which always knows, judges again.
        var context = ReadDesignContext();
        var parsed = NowPlayingLookCheck.ParseEntries(args["looks"], context);
        reasons.AddRange(parsed.Reasons);
        if (reasons.Count > 0) return RejectedLooks(reasons);

        if (_config.LooksRequestPath is null || _config.LooksResultPath is null)
            return Refusal("no_data", "unavailable", "The song look tools are not wired on this server configuration.");

        string id = Guid.NewGuid().ToString("N");
        var now = _clock();
        string json = NowPlayingLookProposal.RequestJson(id, now, parsed.Looks, note).ToJsonString();

        // The widget's own decoder has the last word before anything is queued.
        var decoded = NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(json), now, context);
        if (decoded.Kind == MessageDecodedKind.Refused) return RejectedLooks(decoded.Reasons);
        if (decoded.Kind != MessageDecodedKind.Proposal || json.Length > DeskMaxRequestBytes)
            return RejectedLooks(["the request is too large for the widget to read"]);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_config.LooksRequestPath)!);
            WriteRequestAtomically(_config.LooksRequestPath, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refusal("error", "request_write_failed", $"Could not queue the request ({e.GetType().Name}).");
        }

        var deadline = DateTimeOffset.UtcNow + _config.LooksWaitTimeout;
        while (true)
        {
            if (TryReadResult(_config.LooksResultPath, id) is { } result)
                return LooksAnswer(result, id);
            if (DateTimeOffset.UtcNow >= deadline) break;
            Thread.Sleep(_config.LooksPollInterval);
        }

        // The request stays for the widget's next look within its ten-minute window.
        return new JsonObject
        {
            ["status"] = "queued",
            ["reason"] = "widget_not_responding",
            ["request_id"] = id,
            ["remedy"] = $"The Sanduhr widget did not answer within {(int)_config.LooksWaitTimeout.TotalSeconds} seconds. "
                         + "Start it: it picks the request up within ten minutes of when it was made and shows it as a suggestion.",
        };
    }

    /// <summary>The widget's answer, passed on: its status (one of the three it may give), the reasons for a refusal, how many looks
    /// were saved and whether Style what's playing is on.</summary>
    private static JsonObject LooksAnswer(JsonObject result, string id)
    {
        var answer = new JsonObject();
        string status = result["status"] is JsonValue sv && sv.TryGetValue<string>(out var word) && LooksAnswerStatuses.Contains(word, StringComparer.Ordinal)
            ? word
            : "rejected";
        answer["status"] = status;
        if (result["reasons"] is { } reasons) answer["reasons"] = reasons.DeepClone();
        if (status == "rejected" && answer["reasons"] is null) answer["reason"] = "unexpected_answer";
        if (result["looks_saved"] is { } saved) answer["looks_saved"] = saved.DeepClone();
        if (result["style_on"] is { } styleOn) answer["style_on"] = styleOn.DeepClone();
        answer["request_id"] = id;
        return answer;
    }

    private static JsonObject RejectedLooks(IReadOnlyList<string> reasons)
    {
        var list = new JsonArray();
        foreach (var r in reasons) list.Add((JsonNode?)r);
        return new JsonObject { ["status"] = "rejected", ["reasons"] = list };
    }
}
