using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

// get_desk_messages and propose_desk_messages, the Desk's handwritten line. Ported from the Mac's
// sanduhr_mcp.py (item 54). The checks are Core's own (MessageProposal.Validate and Decode, linked
// here), so the server refuses exactly what the widget would. propose_desk_messages never touches
// messages.txt: it queues desk-messages-request.json, the widget checks it again and holds it as a
// suggestion (or applies it, when the user lets Claude change the messages directly) and answers
// in desk-messages-result.json. get_desk_messages reads messages.txt and the state file the widget
// keeps of the pin and the rotation; it writes nothing.
public sealed partial class ToolLogic
{
    /// <summary>messages.txt is read only up to this size (the Mac's cap).</summary>
    public const int DeskMaxFileBytes = 256 * 1024;

    /// <summary>The request is far below this by construction (60 lines of 120 characters); the cap
    /// is a belt for the day a limit above it moves.</summary>
    public const int DeskMaxRequestBytes = 128 * 1024;

    private static readonly string[] DeskArgs = ["lines", "mode", "note"];

    // -- get_desk_messages ----------------------------------------------------

    public JsonObject BuildDeskMessages()
    {
        if (_config.DeskMessagesPaths is not { Count: > 0 } messagePaths)
            return Refusal("no_data", "unavailable", "Desk messages are not wired on this server configuration.");

        var state = ReadDeskState();
        var limits = new JsonObject
        {
            ["lines_per_proposal"] = MessageProposal.MaxLines,
            ["characters_per_line"] = MessageProposal.MaxLineChars,
        };
        var output = new JsonObject
        {
            ["status"] = "ok",
            ["file_found"] = false,
            ["lines"] = new JsonArray(),
            ["pinned"] = state.Pinned,
            ["rotate"] = state.Hourly ? "hourly" : "daily",
            ["mix_daily"] = state.Mix,
            ["special_mode"] = MessageSpecialModes.Word(state.Special),
            ["special_seconds"] = (int)state.Seconds,
            ["today"] = null,
            ["today_special"] = new JsonArray(),
            ["limits"] = limits,
        };
        if (!state.Known)
            output["settings_note"] = "Sanduhr has not reported the pin and rotation yet; shown as the defaults.";

        // The first copy that exists wins (the Store build's cache copy shadows the real file).
        string messagesPath = messagePaths.FirstOrDefault(File.Exists) ?? messagePaths[^1];
        byte[] data;
        try
        {
            using var fs = new FileStream(messagesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[DeskMaxFileBytes + 1];
            int n = 0, read;
            while (n < buffer.Length && (read = fs.Read(buffer, n, buffer.Length - n)) > 0) n += read;
            data = buffer[..n];
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            output["today"] = state.PinnedLine;
            return output;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return NoData("unreadable", "The Desk's messages.txt could not be read.");
        }
        if (data.Length > DeskMaxFileBytes)
            return NoData("too_large", "The Desk's messages.txt is larger than 256 KB; it is not read.");
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return NoData("not_utf8", "The Desk's messages.txt is not UTF-8; it is not read.");
        }

        var lines = MessageText.SplitLines(text);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        var lineArray = new JsonArray();
        foreach (var l in lines) lineArray.Add((JsonNode?)l);
        output["file_found"] = true;
        output["lines"] = lineArray;

        var today = DeskMessages.Today(text, _clock(), state.Hourly, state.Mix, state.Pinned ? state.PinnedLine : null);
        var special = new JsonArray();
        foreach (var l in today.Special) special.Add((JsonNode?)l);
        output["today_special"] = special;
        output["today"] = today.Usual;
        return output;
    }

    private sealed record DeskState(bool Known, bool Pinned, string? PinnedLine, bool Hourly, bool Mix, MessageSpecialMode Special, double Seconds);

    /// <summary>The widget's note of the user's pin and rotation (<c>desk-messages-state.json</c>), or the
    /// defaults when it has not written one yet.</summary>
    private DeskState ReadDeskState()
    {
        var unknown = new DeskState(false, false, null, false, false, MessageSpecialMode.Stack, MessageSpecialModes.DefaultSeconds);
        try
        {
            if (_config.DeskStatePath is not { } path || !File.Exists(path)) return unknown;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            if (JsonNode.Parse(sr.ReadToEnd()) is not JsonObject root || !McpConfig.IsSchemaV1(root["schema_version"]))
                return unknown;
            bool pinned = ReadBool(root["pinned"]);
            string? line = pinned && TryString(root["pinned_line"], out var pl) ? pl : null;
            bool hourly = TryString(root["rotate"], out var rotate) && rotate == "hourly";
            var special = TryString(root["special_mode"], out var mode) ? MessageSpecialModes.Parse(mode) : MessageSpecialMode.Stack;
            double seconds = root["special_seconds"] is JsonValue sv && sv.TryGetValue<double>(out var sec)
                ? MessageSpecialModes.NormalizeSeconds(sec)
                : MessageSpecialModes.DefaultSeconds;
            return new DeskState(true, pinned, line, hourly, ReadBool(root["mix_daily"]), special, seconds);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return unknown;   // unreadable state = the defaults
        }
    }

    // -- propose_desk_messages ------------------------------------------------

    public JsonObject BuildProposeDeskMessages(JsonObject? args)
    {
        args ??= new JsonObject();
        var reasons = new List<string>();
        var unknown = args.Select(kv => kv.Key).Where(k => !DeskArgs.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            reasons.Add("unknown argument(s): " + string.Join(", ", unknown.Take(5).Select(k => Clip(k, 20))));

        string mode = "add";
        if (args["mode"] is { } m)
        {
            if (TryString(m, out var word) && word is "add" or "replace") mode = word;
            else reasons.Add("mode must be add or replace");
        }

        string? note = null;
        if (args["note"] is { } n)
        {
            if (!TryString(n, out var raw)) reasons.Add("note must be a string");
            else if (raw.EnumerateRunes().Count() > MessageProposal.MaxNoteChars || raw.EnumerateRunes().Any(MessageProposal.IsControl))
                reasons.Add($"note must be one line of at most {MessageProposal.MaxNoteChars} characters");
            else if (MessageText.Trim(raw).Length > 0) note = MessageText.Trim(raw);
        }

        var lines = new List<string>();
        if (args["lines"] is not JsonArray array || array.Count == 0)
        {
            reasons.Add($"lines must be a list of 1 to {MessageProposal.MaxLines} strings");
        }
        else
        {
            var notStrings = new List<string>();
            for (int i = 0; i < array.Count; i++)
            {
                if (TryString(array[i], out var line)) lines.Add(line);
                else if (notStrings.Count < 20 && i < MessageProposal.MaxLines * 2) notStrings.Add($"line {i + 1} is not a string");
            }
            if (notStrings.Count > 0) reasons.AddRange(notStrings);
            else reasons.AddRange(MessageProposal.Validate(lines));
        }
        if (reasons.Count > 0) return Rejected(reasons);

        if (_config.DeskRequestPath is null || _config.DeskResultPath is null)
            return Refusal("no_data", "unavailable", "Desk messages are not wired on this server configuration.");

        string id = Guid.NewGuid().ToString("N");
        var now = _clock();
        var trimmed = new JsonArray();
        foreach (var l in lines) trimmed.Add((JsonNode?)MessageText.Trim(l));
        var request = new JsonObject
        {
            ["schema_version"] = 1,
            ["id"] = id,
            ["requested_at"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["lines"] = trimmed,
            ["mode"] = mode,
            ["note"] = note,
        };
        string json = request.ToJsonString();

        // The widget's own decoder has the last word before anything is queued.
        var decoded = MessageProposal.Decode(Encoding.UTF8.GetBytes(json), now);
        if (decoded.Kind == MessageDecodedKind.Refused) return Rejected(decoded.Reasons);
        if (json.Length > DeskMaxRequestBytes || decoded.Kind != MessageDecodedKind.Proposal)
            return Rejected(["the request is too large for the widget to read"]);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_config.DeskRequestPath)!);
            WriteRequestAtomically(_config.DeskRequestPath, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refusal("error", "request_write_failed", $"Could not queue the request ({e.GetType().Name}).");
        }

        var deadline = DateTimeOffset.UtcNow + _config.DeskWaitTimeout;
        while (true)
        {
            if (TryReadResult(_config.DeskResultPath, id) is { } result)
            {
                var answer = new JsonObject();
                foreach (var key in new[] { "status", "reasons", "lines_added", "lines_skipped", "mode" })
                    if (result[key] is { } value) answer[key] = value.DeepClone();
                answer["request_id"] = id;
                return answer;
            }
            if (DateTimeOffset.UtcNow >= deadline) break;
            Thread.Sleep(_config.DeskPollInterval);
        }

        // The request stays for the widget's next look within its ten-minute window.
        return new JsonObject
        {
            ["status"] = "queued",
            ["reason"] = "widget_not_responding",
            ["request_id"] = id,
            ["remedy"] = $"The Sanduhr widget did not answer within {(int)_config.DeskWaitTimeout.TotalSeconds} seconds. "
                         + "Start it: it picks the request up within ten minutes of when it was made and shows it as a suggestion.",
        };
    }

    private static JsonObject Rejected(IReadOnlyList<string> reasons)
    {
        var list = new JsonArray();
        foreach (var r in reasons) list.Add((JsonNode?)r);
        return new JsonObject { ["status"] = "rejected", ["reasons"] = list };
    }
}
