using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

// get_desk and propose_desk, the agent-designed Desk (3.7.0). The checks are Core's own (DeskDesign.Validate and what it
// composes, linked here), so the server refuses exactly what the widget would. get_desk reads desk-state.json, the
// widget's note of the Desk; it writes nothing. propose_desk never touches the Desk: it validates, queues
// desk-design-request.json, and waits for the widget's answer in desk-design-result.json (previewing, applied, rejected,
// needs_font), or answers queued when the widget does not. A refused design leaves no request file.
public sealed partial class ToolLogic
{
    /// <summary>desk-state.json is read only up to this size (the installed fonts and the Start-menu apps dominate it).</summary>
    public const int DesignMaxStateBytes = 1024 * 1024;

    private static readonly string[] DesignArgs = ["design", "name", "note"];

    /// <summary>What get_desk passes on from the state file: these keys, nothing else.</summary>
    private static readonly string[] DesignStateKeys =
        ["design", "pieces", "anchors", "fonts", "wallpaper", "designs", "direct", "limits", "start_menu_apps"];

    private static readonly string[] DesignAnswerStatuses =
        ["previewing", "applied", "rejected", "needs_font"];

    private enum DesignStateOutcome { Ok, Missing, Unreadable, TooLarge, NotJson, Schema }

    // -- get_desk -------------------------------------------------------------

    public JsonObject BuildGetDesk()
    {
        if (_config.DesignStatePath is null)
            return Refusal("no_data", "unavailable", "The Desk design tools are not wired on this server configuration.");

        var (outcome, state) = ReadDesignState();
        switch (outcome)
        {
            case DesignStateOutcome.Missing:
                return NoData("missing",
                    "Sanduhr has not written the Desk state yet. Start the Sanduhr widget with its Claude Code integration installed "
                    + "(Settings > Claude Code); the state appears within a few seconds. Then call get_desk again.");
            case DesignStateOutcome.TooLarge:
                return NoData("too_large", "The Desk state is larger than 1 MB; it is not read. Restart the Sanduhr widget.");
            case DesignStateOutcome.Schema:
                return NoData("schema_unsupported", "The Desk state is from a different Sanduhr version. Update or restart the Sanduhr widget.");
            case DesignStateOutcome.Unreadable:
                return NoData("unreadable", "The Desk state could not be read right now. Try again in a moment.");
            case DesignStateOutcome.NotJson:
                return NoData("malformed", "The Desk state is not readable JSON. Restart the Sanduhr widget to write it again.");
        }

        var answer = new JsonObject { ["status"] = "ok" };
        foreach (var key in DesignStateKeys)
            if (state![key] is { } value) answer[key] = value.DeepClone();
        if (state!["writer_version"] is JsonValue wv && wv.TryGetValue<string>(out var writer)) answer["writer_version"] = writer;
        try
        {
            double age = (_clock() - new DateTimeOffset(File.GetLastWriteTimeUtc(_config.DesignStatePath), TimeSpan.Zero)).TotalSeconds;
            answer["state_age_seconds"] = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Round(age)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The age is a courtesy.
        }
        return answer;
    }

    /// <summary>The state file, read with a few quick retries (the widget replaces it in place).</summary>
    private (DesignStateOutcome, JsonObject?) ReadDesignState()
    {
        string? path = _config.DesignStatePath;
        if (path is null) return (DesignStateOutcome.Missing, null);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return (DesignStateOutcome.Missing, null);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[DesignMaxStateBytes + 1];
                int n = 0, read;
                while (n < buffer.Length && (read = fs.Read(buffer, n, buffer.Length - n)) > 0) n += read;
                if (n > DesignMaxStateBytes) return (DesignStateOutcome.TooLarge, null);
                if (JsonNode.Parse(new ReadOnlySpan<byte>(buffer, 0, n)) is not JsonObject root) return (DesignStateOutcome.NotJson, null);
                return McpConfig.IsSchemaV1(root["schema_version"]) ? (DesignStateOutcome.Ok, root) : (DesignStateOutcome.Schema, null);
            }
            catch (JsonException)
            {
                return (DesignStateOutcome.NotJson, null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt < 3) Thread.Sleep(25);
            }
        }
        return (DesignStateOutcome.Unreadable, null);
    }

    /// <summary>What the machine adds to the design rules, from the state file; any part it lacks is unknown (its rule is
    /// skipped here and the widget, which knows, checks again).</summary>
    private DeskDesignContext ReadDesignContext()
    {
        var (outcome, state) = ReadDesignState();
        if (outcome != DesignStateOutcome.Ok || state is null) return DeskDesignContext.Unknown;

        IReadOnlyCollection<string>? fonts = null;
        if (state["fonts"] is JsonObject fo && fo["installed"] is JsonArray installed)
            fonts = Strings(installed, 5000, null);

        IReadOnlyCollection<string>? apps = null;
        if (state["start_menu_apps"] is JsonArray appArray)
            apps = Strings(appArray, 5000, "id");

        WallpaperOverview? wallpaper = state["wallpaper"] is JsonObject wo ? WallpaperOverview.FromJson(wo["summary"]) : null;
        DeskDesign? current = state["design"] is JsonObject d ? DeskDesign.Parse(d).Design : null;
        return new DeskDesignContext(fonts, wallpaper, apps, current);
    }

    private static List<string> Strings(JsonArray array, int max, string? member)
    {
        var list = new List<string>();
        foreach (var item in array.Take(max))
        {
            JsonNode? n = member is null ? item : (item as JsonObject)?[member];
            if (n is JsonValue v && v.TryGetValue<string>(out var s) && s is not null) list.Add(s);
        }
        return list;
    }

    // -- propose_desk ---------------------------------------------------------

    public JsonObject BuildProposeDesk(JsonObject? args)
    {
        args ??= new JsonObject();
        var reasons = new List<DeskReason>();
        var unknown = args.Select(kv => kv.Key).Where(k => !DesignArgs.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            reasons.Add(new DeskReason("arguments", "unknown argument(s): " + string.Join(", ", unknown.Take(5).Select(k => Clip(k, 20)))
                + ". The arguments are design, name and note."));
        var given = args["design"] as JsonObject;
        if (given is null)
            reasons.Add(new DeskReason("design", "must be an object: a partial Desk design (see the tool description)"));
        if (reasons.Count > 0) return RejectedDesign(reasons, []);

        // name and note on the call are the design's own, and win over any inside it.
        var proposal = (JsonObject)given!.DeepClone();
        if (args["name"] is { } name) proposal["name"] = name.DeepClone();
        if (args["note"] is { } note) proposal["note"] = note.DeepClone();

        var verdict = DeskDesign.Validate(proposal, ReadDesignContext());
        if (verdict.Status == DeskDesignStatus.Rejected) return RejectedDesign(verdict.Reasons, verdict.Warnings);

        if (_config.DesignRequestPath is null || _config.DesignResultPath is null)
            return Refusal("no_data", "unavailable", "The Desk design tools are not wired on this server configuration.");

        string id = Guid.NewGuid().ToString("N");
        var request = new JsonObject
        {
            ["schema_version"] = 1,
            ["id"] = id,
            ["requested_at"] = _clock().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["design"] = verdict.Design!.ToJson(),
        };
        string json = request.ToJsonString();
        if (json.Length > DeskDesign.MaxJsonBytes)
            return RejectedDesign([new DeskReason("design", "is too large for the widget to read")], []);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_config.DesignRequestPath)!);
            WriteRequestAtomically(_config.DesignRequestPath, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refusal("error", "request_write_failed", $"Could not queue the request ({e.GetType().Name}).");
        }

        var deadline = DateTimeOffset.UtcNow + _config.DesignWaitTimeout;
        while (true)
        {
            if (TryReadResult(_config.DesignResultPath, id) is { } result)
                return DesignAnswer(result, verdict, id);
            if (DateTimeOffset.UtcNow >= deadline) break;
            Thread.Sleep(_config.DesignPollInterval);
        }

        // The request stays for the widget's next look within its ten-minute window.
        var queued = new JsonObject
        {
            ["status"] = "queued",
            ["reason"] = "widget_not_responding",
            ["request_id"] = id,
            ["remedy"] = $"The Sanduhr widget did not answer within {(int)_config.DesignWaitTimeout.TotalSeconds} seconds. "
                         + "Start it: it picks the request up within ten minutes of when it was made and shows the design for the user to review.",
        };
        if (verdict.Fonts.Count > 0) queued["fonts"] = verdict.FontsJson();
        if (verdict.Warnings.Count > 0) queued["warnings"] = verdict.WarningsJson();
        return queued;
    }

    /// <summary>The widget's answer, passed on: its status (one of the four it may give), reasons, fonts, the
    /// what-changed lines and warnings; the fonts and warnings this server found stand in when the widget gave none.</summary>
    private static JsonObject DesignAnswer(JsonObject result, DeskValidation verdict, string id)
    {
        var answer = new JsonObject();
        string status = result["status"] is JsonValue sv && sv.TryGetValue<string>(out var word) && DesignAnswerStatuses.Contains(word, StringComparer.Ordinal)
            ? word
            : "rejected";
        answer["status"] = status;
        foreach (var key in new[] { "reason", "reasons", "fonts", "changes", "warnings" })
            if (result[key] is { } value) answer[key] = value.DeepClone();
        if (status == "rejected" && answer["reasons"] is null && answer["reason"] is null)
            answer["reason"] = "unexpected_answer";
        if (answer["fonts"] is null && verdict.Fonts.Count > 0) answer["fonts"] = verdict.FontsJson();
        if (answer["warnings"] is null && verdict.Warnings.Count > 0) answer["warnings"] = verdict.WarningsJson();
        answer["request_id"] = id;
        return answer;
    }

    private static JsonObject RejectedDesign(IReadOnlyList<DeskReason> reasons, IReadOnlyList<DeskReason> warnings)
    {
        var list = new JsonArray();
        foreach (var r in reasons) list.Add((JsonNode?)r.ToJson());
        var answer = new JsonObject { ["status"] = "rejected", ["reasons"] = list };
        if (warnings.Count > 0)
        {
            var w = new JsonArray();
            foreach (var r in warnings) w.Add((JsonNode?)r.ToJson());
            answer["warnings"] = w;
        }
        return answer;
    }
}
