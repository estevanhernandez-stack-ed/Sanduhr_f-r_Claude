using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>Whether a proposal appends to messages.txt or replaces its lines.</summary>
public enum MessageProposalMode { Add, Replace }

/// <summary>What the handoff tells the MCP server's caller.</summary>
public enum MessageProposalStatus { Applied, PendingApproval, Rejected }

/// <summary>What Add (or the direct opt-in) makes of the existing file.</summary>
/// <param name="Text">messages.txt as it will be written.</param>
public sealed record MessageApplied(string Text, int Added, int Skipped);

/// <summary>What <see cref="MessageProposal.Decode"/> made of a request file.</summary>
public enum MessageDecodedKind
{
    /// <summary>A request that passed the checks.</summary>
    Proposal,
    /// <summary>Readable enough to answer: the result names the id.</summary>
    Refused,
    /// <summary>Not a request (unreadable, no id) or too old: dropped without an answer.</summary>
    Discard,
}

/// <summary>The outcome of reading one request.</summary>
public sealed class MessageDecoded
{
    private MessageDecoded(MessageDecodedKind kind, MessageProposal? proposal, string? id, IReadOnlyList<string> reasons)
    {
        Kind = kind;
        Proposal = proposal;
        Id = id;
        Reasons = reasons;
    }

    public MessageDecodedKind Kind { get; }
    public MessageProposal? Proposal { get; }
    /// <summary>The request id, for a proposal or a refusal.</summary>
    public string? Id { get; }
    public IReadOnlyList<string> Reasons { get; }

    public static MessageDecoded Discard { get; } = new(MessageDecodedKind.Discard, null, null, []);
    public static MessageDecoded Of(MessageProposal p) => new(MessageDecodedKind.Proposal, p, p.Id, []);
    public static MessageDecoded Refuse(string id, IReadOnlyList<string> reasons) => new(MessageDecodedKind.Refused, null, id, reasons);
}

/// <summary>
/// Claude's suggested Desk messages, the pure half: the request the MCP server's
/// <c>propose_desk_messages</c> writes, the checks the app runs again (it is the authority), what Add
/// does to messages.txt, and the result the server waits for. <see cref="DeskMessageHandoff"/> does the
/// files. Ported from the Mac's Desk/MessageProposal.swift. The server mirrors the file names, the
/// limits and the reasons' wording; a test on each side pins them.
/// </summary>
public sealed record MessageProposal(string Id, DateTimeOffset RequestedAt, IReadOnlyList<string> Lines, MessageProposalMode Mode, string? Note)
{
    public const string RequestFile = "desk-messages-request.json";
    public const string ResultFile = "desk-messages-result.json";
    public const string StateFile = "desk-messages-state.json";

    public const int MaxLines = 60;
    public const int MaxLineChars = 120;
    public const int MaxNoteChars = 300;
    /// <summary>A request is interactive: one older than this (Sanduhr was not running) is dropped.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(600);
    /// <summary>Add never grows messages.txt past this many lines.</summary>
    public const int MaxFileLines = 1000;

    /// <summary>The lines that show on the Desk (not blank, not a comment).</summary>
    public IReadOnlyList<string> MessageLines => Lines.Where(IsMessageLine).ToList();

    public bool Equals(MessageProposal? other) =>
        other is not null && Id == other.Id && RequestedAt == other.RequestedAt && Mode == other.Mode
        && Note == other.Note && MessageText.SequenceEqual(Lines, other.Lines);

    public override int GetHashCode() => HashCode.Combine(Id, RequestedAt, Mode, Note, MessageText.SequenceHash(Lines));

    public static bool IsMessageLine(string line)
    {
        string t = MessageText.Trim(line);
        return t.Length > 0 && !t.StartsWith('#');
    }

    // Reading the request

    private static readonly string[] StampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];
    private static readonly Regex LongFraction = new(@"(\.\d{7})\d+", RegexOptions.CultureInvariant);

    public static MessageDecoded Decode(byte[] data, DateTimeOffset now)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(data); }
        catch (JsonException) { return MessageDecoded.Discard; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return MessageDecoded.Discard;
            if (!TryString(root, "id", out string? id) || id.Length == 0
                || new StringInfo(id).LengthInTextElements > 64)
                return MessageDecoded.Discard;
            if (!TryString(root, "requested_at", out string? stamp) || ParseDate(stamp) is not { } at)
                return MessageDecoded.Refuse(id, ["requested_at is missing or not a date"]);
            if (now - at > MaxAge) return MessageDecoded.Discard;
            if (SchemaVersion(root) != 1)
                return MessageDecoded.Refuse(id, ["this Sanduhr reads requests of schema 1"]);

            var reasons = new List<string>();
            string modeText = TryString(root, "mode", out string? m) ? m : "add";
            MessageProposalMode? mode = modeText switch
            {
                "add" => MessageProposalMode.Add,
                "replace" => MessageProposalMode.Replace,
                _ => null,
            };
            if (mode is null) reasons.Add("mode must be add or replace");

            string? note = null;
            if (root.TryGetProperty("note", out var rawNote) && rawNote.ValueKind != JsonValueKind.Null)
            {
                if (rawNote.ValueKind == JsonValueKind.String && rawNote.GetString() is { } s
                    && CountScalars(s) <= MaxNoteChars && !HasControl(s))
                {
                    string t = MessageText.Trim(s);
                    note = t.Length == 0 ? null : t;
                }
                else
                {
                    reasons.Add($"note must be one line of at most {MaxNoteChars} characters");
                }
            }

            var strings = new List<string>();
            int rawCount = 0;
            if (root.TryGetProperty("lines", out var rawLines) && rawLines.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rawLines.EnumerateArray())
                {
                    rawCount++;
                    if (item.ValueKind == JsonValueKind.String) strings.Add(item.GetString()!);
                }
            }
            if (strings.Count != rawCount) reasons.Add($"lines must be a list of 1 to {MaxLines} strings");
            else reasons.AddRange(Validate(strings));

            if (reasons.Count > 0 || mode is null)
                return MessageDecoded.Refuse(id, reasons.Take(20).ToList());
            var lines = strings.Select(MessageText.Trim).ToList();
            return MessageDecoded.Of(new MessageProposal(id, at, lines, mode.Value, note));
        }
    }

    private static bool TryString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        if (root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String && e.GetString() is { } s)
        {
            value = s;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Swift's <c>root["schema_version"] as? Int ?? 1</c>: only a whole number counts.</summary>
    private static long SchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schema_version", out var e) || e.ValueKind != JsonValueKind.Number) return 1;
        if (e.TryGetInt64(out long n)) return n;
        double d = e.GetDouble();
        return d == Math.Floor(d) && Math.Abs(d) < 1e15 ? (long)d : 1;
    }

    /// <summary>The request stamp: ISO 8601 with or without a fraction (also .NET's seven digits and longer).</summary>
    public static DateTimeOffset? ParseDate(string s)
    {
        s = LongFraction.Replace(s, "$1");
        return DateTimeOffset.TryParseExact(s, StampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    // The checks

    /// <summary>Why the lines are refused, empty when they may be applied. Same rules and wording as the server.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return [$"lines must be a list of 1 to {MaxLines} strings"];
        var reasons = new List<string>();
        if (lines.Count > MaxLines) reasons.Add($"{lines.Count} lines; the limit is {MaxLines}");
        for (int i = 0; i < Math.Min(lines.Count, MaxLines * 2); i++)
            if (Check(lines[i]) is { } why) reasons.Add($"line {i + 1} {why}");
        if (reasons.Count == 0 && !lines.Any(IsMessageLine))
            reasons.Add("no message line: every line is blank or a # comment");
        return reasons.Take(20).ToList();
    }

    public static IReadOnlyList<string> Weekdays => DeskMessages.Weekdays;

    private static readonly HashSet<string> WeekdayLookalikes = new(
        DeskMessages.Weekdays.Select(d => d.ToLowerInvariant())
            .Concat(["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "tues", "thur", "thurs"]),
        StringComparer.Ordinal);

    private static readonly int[] DaysInMonth = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    private static readonly Regex ShortDate = new(@"^\d{1,2}-\d{1,2}$", RegexOptions.CultureInvariant);

    /// <summary>U+0000 to 001F, U+007F to 009F and the Unicode line and paragraph separators.</summary>
    public static bool IsControl(Rune r) =>
        r.Value < 0x20 || r.Value is >= 0x7F and <= 0x9F || r.Value == 0x2028 || r.Value == 0x2029;

    private static bool HasControl(string s)
    {
        foreach (var r in s.EnumerateRunes())
            if (IsControl(r)) return true;
        return false;
    }

    private static int CountScalars(string s) => s.EnumerateRunes().Count();

    /// <summary>One line: null when good, else why not (the caller adds "line N").</summary>
    public static string? Check(string line)
    {
        if (HasControl(line)) return "has a control character or a line break";
        string text = MessageText.Trim(line);
        int count = CountScalars(text);
        if (count > MaxLineChars) return $"is {count} characters; the limit is {MaxLineChars}";
        if (text.Length == 0 || text.StartsWith('#')) return null;
        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            string tag = MessageText.Trim(text[..colon]);
            if (ShortDate.IsMatch(tag))
            {
                var parts = tag.Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => int.TryParse(p, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? (int?)n : null)
                    .Where(n => n is not null).Select(n => n!.Value).ToArray();
                bool valid = tag.Length == 5 && parts.Length == 2 && parts[0] is >= 1 and <= 12
                    && parts[1] >= 1 && parts[1] <= DaysInMonth[Math.Max(0, Math.Min(11, parts[0] - 1))];
                if (!valid) return $"starts with '{tag}:', which is not a date; write MM-DD, like 10-31";
            }
            else if (WeekdayLookalikes.Contains(tag.ToLowerInvariant()) && !Weekdays.Contains(tag))
            {
                return $"starts with '{MessageText.Prefix(tag, 12)}:'; write the day as Mon, Tue, Wed, Thu, Fri, Sat or Sun";
            }
        }
        var split = DeskMessages.SplitPrefix(text);
        if (split.Kind != MessagePrefixKind.Plain && split.Body.Length == 0) return "has a prefix but no text";
        var strict = MessageMarkup.ParseStrict(split.Body);
        if (strict.Failure is { } failure) return failure.Reason;
        return null;
    }

    // Applying

    /// <summary>
    /// What Add (or the direct opt-in) makes of <paramref name="existing"/>, or null when the list would
    /// pass <see cref="MaxFileLines"/>.
    /// Add: the lines are appended; a message line already in the list (or twice in the proposal) is
    /// skipped. Replace: the comment block at the top of the file (the # and blank lines before the first
    /// message) stays, as the notes on how the file works; every other line, comments among the old
    /// messages included, is replaced. Either way the caller keeps the previous file as messages.txt.previous.
    /// </summary>
    public static MessageApplied? Apply(MessageProposal p, string existing)
    {
        var old = MessageText.SplitLines(existing);
        while (old.Count > 0 && MessageText.Trim(old[^1]).Length == 0) old.RemoveAt(old.Count - 1);
        List<string> output;
        int added = 0, skipped = 0;
        if (p.Mode == MessageProposalMode.Add)
        {
            output = [.. old];
            var seen = new HashSet<string>(old.Where(IsMessageLine).Select(MessageText.Trim), StringComparer.Ordinal);
            foreach (var line in p.Lines)
            {
                if (IsMessageLine(line))
                {
                    if (seen.Contains(line)) { skipped++; continue; }
                    seen.Add(line);
                    added++;
                }
                output.Add(line);
            }
        }
        else
        {
            output = old.TakeWhile(l => !IsMessageLine(l)).ToList();
            while (output.Count > 0 && MessageText.Trim(output[^1]).Length == 0) output.RemoveAt(output.Count - 1);
            if (output.Count > 0) output.Add("");
            output.AddRange(p.Lines);
            added = p.MessageLines.Count;
        }
        while (output.Count > 0 && MessageText.Trim(output[^1]).Length == 0) output.RemoveAt(output.Count - 1);
        if (output.Count > MaxFileLines) return null;
        return new MessageApplied(string.Join("\n", output) + "\n", added, skipped);
    }

    // The result

    public static string Word(MessageProposalStatus s) => s switch
    {
        MessageProposalStatus.Applied => "applied",
        MessageProposalStatus.PendingApproval => "pending_approval",
        _ => "rejected",
    };

    public static string Word(MessageProposalMode m) => m == MessageProposalMode.Add ? "add" : "replace";

    private static readonly JsonWriterOptions Compact = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The result file's contents, keys sorted: <c>{id, completed_at, result: {status, mode, reasons?,
    /// lines_added?, lines_skipped?}}</c>.
    /// </summary>
    public static byte[] ResultJson(string id, MessageProposalStatus status, MessageProposalMode? mode,
        IReadOnlyList<string>? reasons = null, MessageApplied? applied = null, DateTimeOffset? now = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Compact))
        {
            w.WriteStartObject();
            w.WriteString("completed_at", (now ?? DateTimeOffset.UtcNow).UtcDateTime
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            w.WriteString("id", id);
            w.WriteStartObject("result");
            if (applied is not null)
            {
                w.WriteNumber("lines_added", applied.Added);
                w.WriteNumber("lines_skipped", applied.Skipped);
            }
            if (mode is { } m) w.WriteString("mode", Word(m));
            if (reasons is { Count: > 0 })
            {
                w.WriteStartArray("reasons");
                foreach (var r in reasons) w.WriteStringValue(r);
                w.WriteEndArray();
            }
            w.WriteString("status", Word(status));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// desk-messages-state.json: what get_desk_messages reports about the user's settings. The pinned
    /// line is written only while one is pinned; it is on the desktop already. <c>mix_daily</c>: every-day
    /// lines take turns with a weekday's own lines. <c>special_mode</c> and <c>special_seconds</c>: On
    /// special days and its timing. Keys sorted.
    /// </summary>
    public static byte[] StateJson(string? pinned, string rotate, bool mixDaily = false,
        MessageSpecialMode specialMode = MessageSpecialMode.Stack, double specialSeconds = MessageSpecialModes.DefaultSeconds)
    {
        string? line = string.IsNullOrEmpty(pinned) ? null : pinned;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Compact))
        {
            w.WriteStartObject();
            w.WriteBoolean("mix_daily", mixDaily);
            w.WriteBoolean("pinned", line is not null);
            if (line is null) w.WriteNull("pinned_line"); else w.WriteString("pinned_line", line);
            w.WriteString("rotate", rotate == "hourly" ? "hourly" : "daily");
            w.WriteNumber("schema_version", 1);
            w.WriteString("special_mode", MessageSpecialModes.Word(specialMode));
            w.WriteNumber("special_seconds", (int)specialSeconds);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}

/// <summary>The suggestion's notice text. Whether a banner shows follows the alert settings and is the App's call.</summary>
public static class MessageSuggestionNotice
{
    /// <summary>"Claude suggested 2 Desk messages".</summary>
    public static string Title(MessageProposal p)
    {
        int n = p.MessageLines.Count;
        return $"Claude suggested {n} Desk message{(n == 1 ? "" : "s")}";
    }
}
