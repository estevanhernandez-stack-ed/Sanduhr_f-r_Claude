using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

// watch_start, watch_update, watch_end: a live card on the user's Desk for work in flight (a CI run,
// a deploy). Each call queues one request file, watch-request-<ms>-<seq>-<id>.json, in the widget's
// external folder; the widget reads and deletes it, checks it again with the same Core code
// (WatcherRequest.Decode, linked here), and keeps the board in memory. The server answers at once:
// there is no result file to wait for. Nothing is written while watchers.json, which the widget
// writes from the user's "Let agents show watchers" switch, lacks "agents": true. The ids this
// server started are remembered for the session, so an update to an unknown id is refused here.
// Ported from the Mac's sanduhr_mcp.py (item 66).
public sealed partial class ToolLogic
{
    private readonly HashSet<string> _watchStarted = new(StringComparer.Ordinal);
    private readonly HashSet<string> _watchEnded = new(StringComparer.Ordinal);
    private int _watchSeq;

    private static readonly Regex WatchIdPattern = new("^w[0-9a-f]{12}$", RegexOptions.CultureInvariant);

    public JsonObject BuildWatchStart(JsonObject? args)
    {
        var a = args ?? new JsonObject();
        if (WatchChecks(a, ["title", "short", "link", "total", "work"]) is { } bad) return bad;
        var errors = new List<string>();
        var title = WatchLine(a["title"], "title", WatcherLimits.Title, required: true, errors);
        var @short = WatchLine(a["short"], "short", WatcherLimits.Short, required: false, errors);
        var link = WatchLink(a["link"], errors);
        var total = WatchInt(a["total"], "total", 1, WatcherLimits.Total, errors);
        bool work = false;
        if (a["work"] is { } w)
        {
            if (TryBool(w, out bool b)) work = b;
            else errors.Add("work must be true or false");
        }
        if (errors.Count > 0) return WatchInvalid(errors);

        string id = "w" + Guid.NewGuid().ToString("N")[..12];
        var command = new WatcherCommand.Start(id, title!, link, total, work, @short);
        if (QueueWatch(command, id) is { } failed) return failed;
        _watchStarted.Add(id);
        return new JsonObject
        {
            ["status"] = "ok",
            ["id"] = id,
            ["note"] = "Sanduhr shows the watcher while it runs. Update it at least every 10 minutes, and end it with watch_end.",
        };
    }

    public JsonObject BuildWatchUpdate(JsonObject? args)
    {
        var a = args ?? new JsonObject();
        if (WatchChecks(a, ["id", "done", "note", "state"]) is { } bad) return bad;
        if (WatchKnown(a["id"]) is { } unknown) return unknown;
        var errors = new List<string>();
        var done = WatchInt(a["done"], "done", 0, WatcherLimits.Total, errors);
        var note = WatchLine(a["note"], "note", WatcherLimits.Note, required: false, errors);
        WatcherState? state = null;
        if (a["state"] is { } s)
        {
            if (TryString(s, out var word) && word == "running") state = WatcherState.Running;
            else if (word == "waiting") state = WatcherState.Waiting;
            else errors.Add("state must be running or waiting");
        }
        if (errors.Count > 0) return WatchInvalid(errors);

        string id = (string)a["id"]!;
        if (QueueWatch(new WatcherCommand.Update(id, done, note, state), id) is { } failed) return failed;
        return new JsonObject { ["status"] = "ok", ["id"] = id };
    }

    public JsonObject BuildWatchEnd(JsonObject? args)
    {
        var a = args ?? new JsonObject();
        if (WatchChecks(a, ["id", "result", "note"]) is { } bad) return bad;
        if (WatchKnown(a["id"]) is { } unknown) return unknown;
        var errors = new List<string>();
        WatcherState result = WatcherState.Passed;
        if (a["result"] is { } r && TryString(r, out var word) && word is "passed" or "failed")
            result = word == "passed" ? WatcherState.Passed : WatcherState.Failed;
        else
            errors.Add("result must be passed or failed");
        var note = WatchLine(a["note"], "note", WatcherLimits.Note, required: false, errors);
        if (errors.Count > 0) return WatchInvalid(errors);

        string id = (string)a["id"]!;
        if (QueueWatch(new WatcherCommand.End(id, result, note), id) is { } failed) return failed;
        _watchEnded.Add(id);
        return new JsonObject { ["status"] = "ok", ["id"] = id };
    }

    // -- checks ---------------------------------------------------------------

    private static JsonObject WatchRefusal(string reason, string remedy) => Refusal("rejected", reason, remedy);

    private static JsonObject WatchInvalid(List<string> errors) =>
        WatchRefusal("invalid_params", string.Join("; ", errors) + ".");

    /// <summary>The refusals every watch tool shares: not an object, an argument we do not take,
    /// no folder to hand the request to, the user's switch off.</summary>
    private JsonObject? WatchChecks(JsonObject args, string[] allowed)
    {
        var unknown = args.Select(kv => kv.Key).Where(k => !allowed.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            return WatchRefusal("invalid_params",
                "Unknown argument(s): " + string.Join(", ", unknown.Take(5).Select(k => Clip(k, 20))) + ". Takes " + string.Join(", ", allowed) + ".");
        if (_config.ExternalDir is null)
            return Refusal("no_data", "unavailable", "Watchers are not wired on this server configuration.");
        if (!WatchersAllowed())
            return WatchRefusal("watchers_off",
                "The user has not turned on 'Let Claude show watchers' in Sanduhr (Settings, Watchers). "
                + "Do not retry; tell the user if they asked for a watcher.");
        return null;
    }

    private bool WatchersAllowed()
    {
        try
        {
            string path = Path.Combine(_config.ExternalDir!, WatcherRequest.SwitchFile);
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[WatcherRequest.MaxBytes + 1];
            int n = fs.Read(buffer, 0, buffer.Length);
            return WatcherRequest.SwitchAllows(buffer[..n]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;   // unreadable switch = off (fail closed)
        }
    }

    private JsonObject? WatchKnown(JsonNode? idNode)
    {
        if (!TryString(idNode, out var id) || !WatchIdPattern.IsMatch(id))
            return WatchRefusal("invalid_params", "id must be the id watch_start returned.");
        if (!_watchStarted.Contains(id))
            return WatchRefusal("unknown_id", "No watcher with that id was started in this session; call watch_start.");
        if (_watchEnded.Contains(id))
            return WatchRefusal("ended", "That watcher has ended; call watch_start for a new one.");
        return null;
    }

    /// <summary>One line of at most <paramref name="cap"/> characters (code points, as the Mac counts them).
    /// Null when absent or blank; a problem goes to <paramref name="errors"/>.</summary>
    private static string? WatchLine(JsonNode? v, string field, int cap, bool required, List<string> errors)
    {
        if (v is null)
        {
            if (required) errors.Add($"{field} is required");
            return null;
        }
        if (!TryString(v, out var raw))
        {
            errors.Add($"{field} must be a string");
            return null;
        }
        string s = raw.Trim();
        if (required && s.Length == 0)
        {
            errors.Add($"{field} must not be empty");
            return null;
        }
        if (s.EnumerateRunes().Count() > cap || s.EnumerateRunes().Any(MessageProposal.IsControl))
        {
            errors.Add($"{field} must be one line of at most {cap} characters");
            return null;
        }
        return s.Length == 0 ? null : s;
    }

    private static int? WatchInt(JsonNode? v, string field, int lo, int hi, List<string> errors)
    {
        if (v is null) return null;
        if (v is JsonValue jv && jv.TryGetValue<int>(out int n) && n >= lo && n <= hi) return n;
        errors.Add($"{field} must be a whole number from {lo} to {hi}");
        return null;
    }

    /// <summary>An https link with a host and no user or password, at most 2048 characters, no blanks.</summary>
    private static Uri? WatchLink(JsonNode? v, List<string> errors)
    {
        if (v is null) return null;
        if (TryString(v, out var s) && s.Length > 0 && !s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            && WatcherLimits.Link(s) is { } url)
            return url;
        errors.Add($"link must be an https:// link of at most {WatcherLimits.LinkLength} characters, with no user or password");
        return null;
    }

    private static bool TryString(JsonNode? n, out string value)
    {
        value = "";
        if (n is JsonValue v && v.TryGetValue<string>(out var s) && s is not null)
        {
            value = s;
            return true;
        }
        return false;
    }

    private static bool TryBool(JsonNode? n, out bool value)
    {
        value = false;
        return n is JsonValue v && v.TryGetValue<bool>(out value);
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];

    // -- the request file -----------------------------------------------------

    /// <summary>Encodes the command with Core's own writer, proves the widget's decoder accepts the
    /// result, and queues it. Null on success, else the typed failure.</summary>
    private JsonObject? QueueWatch(WatcherCommand command, string id)
    {
        var now = _clock();
        // No folder path goes in the file: the widget has no work-account tag to match it against
        // (the agent's own work flag is all it reads).
        byte[] bytes = WatcherRequest.ToJson(command, now);
        if (WatcherRequest.Decode(bytes, now) is null)
            return Refusal("error", "request_invalid",
                "The request would not pass the widget's own checks (too large or malformed); nothing was queued.");
        string path = Path.Combine(_config.ExternalDir!, WatcherRequest.RequestFileName(now, ++_watchSeq, id));
        try
        {
            Directory.CreateDirectory(_config.ExternalDir!);
            WriteRequestAtomically(path, Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refusal("error", "request_write_failed", $"Could not hand the watcher to Sanduhr ({e.GetType().Name}).");
        }
        return null;
    }
}
