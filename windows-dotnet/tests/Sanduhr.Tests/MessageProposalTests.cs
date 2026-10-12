using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's DeskMessageTests.swift: the checks on a proposal (the MCP server's wording),
/// Add and Replace, and the result and state files. Skipped: <c>serverAgrees</c> (it reads the Python
/// server's source) and <c>noticeFollowsTheAlertSettings</c> (the Mac's Notifier; only the notice title is
/// ported, and tested here).
/// </summary>
public class MessageProposalTests
{
    internal static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_791_133_000);

    internal static MessageProposal Proposal(string[] lines, MessageProposalMode mode = MessageProposalMode.Add, string? note = null) =>
        new("abc", T0, lines, mode, note);

    internal static byte[] RequestJson(string id = "req1", DateTimeOffset? at = null, JsonNode? lines = null, string mode = "add",
        JsonNode? note = null, bool hasNote = false, Action<JsonObject>? extra = null)
    {
        var root = new JsonObject
        {
            ["schema_version"] = 1,
            ["id"] = id,
            ["requested_at"] = (at ?? T0).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["lines"] = lines ?? new JsonArray("{glow} hi."),
            ["mode"] = mode,
        };
        if (hasNote || note is not null) root["note"] = note;
        extra?.Invoke(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static JsonObject Parse(byte[] data) => (JsonObject)JsonNode.Parse(data)!;

    [Fact]
    public void GoodLines_Pass()
    {
        string[] good =
        [
            "keep building.", "Mon: one thing at a time.", "10-31: {ink:#ff7518,#6b2fa0} {write} boo.",
            "{ink:#ff2a6d,#05d9e8} {glow} hello", "{size:0.5}{noglow} small.", "{SHIMMER} loud", "# a note",
            "", "Note: a colon in a plain line.", "02-29: leap.", "{ink:fff} three digits.",
            new string('x', 120), "hello {glow} mid-line braces are text", "ünïcödé ✨ fine.",
        ];
        Assert.Empty(MessageProposal.Validate(good));
    }

    /// <summary>The same cases and wording as the MCP server's tests (test_bad_lines_are_named).</summary>
    [Fact]
    public void BadLines_AreNamed()
    {
        (string Line, string Want)[] cases =
        [
            (new string('x', 121), "121 characters"),
            ("tab\there", "control character"),
            ("line\nbreak", "control character"),
            ("sep" + (char)0x2028 + "arator", "control character"),
            ("13-01: no such month", "not a date"),
            ("1-5: short date", "not a date"),
            ("02-30: no such day", "not a date"),
            ("Monday: long name", "write the day as Mon"),
            ("mon: lowercase", "write the day as Mon"),
            ("Mon:   ", "prefix but no text"),
            ("{blink} hi", "unknown effect {blink}"),
            ("{ink:#zzzzzz} hi", "not hex"),
            ("{ink:} hi", "1 to 4 hex colors"),
            ("{ink:#111,#222,#333,#444,#555} hi", "at most 4"),
            ("{size:3} big", "0.5 to 2"),
            ("{size:big} big", "0.5 to 2"),
            ("{glow:yes} hi", "takes no value"),
            ("{glow hi", "not closed"),
            ("{glow} {write}", "no text"),
        ];
        foreach (var (line, want) in cases)
        {
            var reasons = MessageProposal.Validate([line]);
            Assert.True(reasons.Count == 1, line);
            Assert.StartsWith("line 1 ", reasons[0]);
            Assert.True(reasons[0].Contains(want), $"{line}: {string.Join("; ", reasons)}");
        }
    }

    [Fact]
    public void CountsAndEmptyLists()
    {
        Assert.Contains("61 lines", MessageProposal.Validate(Enumerable.Repeat("a", 61).ToArray())[0]);
        Assert.Empty(MessageProposal.Validate(Enumerable.Repeat("a", 60).ToArray()));
        Assert.Contains("list", MessageProposal.Validate([])[0]);
        Assert.Contains("no message line", MessageProposal.Validate(["# only", ""])[0]);
    }

    /// <summary>The file names and limits the MCP server mirrors literally.</summary>
    [Fact]
    public void NamesAndLimits_AreTheMacs()
    {
        Assert.Equal("desk-messages-request.json", MessageProposal.RequestFile);
        Assert.Equal("desk-messages-result.json", MessageProposal.ResultFile);
        Assert.Equal("desk-messages-state.json", MessageProposal.StateFile);
        Assert.Equal(60, MessageProposal.MaxLines);
        Assert.Equal(120, MessageProposal.MaxLineChars);
        Assert.Equal(300, MessageProposal.MaxNoteChars);
        Assert.Equal(1000, MessageProposal.MaxFileLines);
        Assert.Equal(TimeSpan.FromMinutes(10), MessageProposal.MaxAge);
        Assert.Equal("ink, glow, noglow, size, write, shimmer, sweep, font", MessageMarkup.EffectNames);
        Assert.Equal(2, MessageMarkup.SweepPeriodMin);
        Assert.Equal(3600, MessageMarkup.SweepPeriodMax);
    }

    [Fact]
    public void LimitsCountScalars_NotUtf16Units()
    {
        // 120 astral characters are 240 UTF-16 units but 120 scalars: within the limit.
        string astral = string.Concat(Enumerable.Repeat("𝐚", 120));
        Assert.Empty(MessageProposal.Validate([astral]));
        Assert.Contains("121 characters", MessageProposal.Validate([astral + "𝐚"])[0]);
    }

    // Requests and apply

    [Fact]
    public void DecodesAGoodRequest()
    {
        var d = MessageProposal.Decode(RequestJson(lines: new JsonArray("  a.  ", "Fri: b."), mode: "replace", note: " why "), T0);
        Assert.Equal(MessageDecodedKind.Proposal, d.Kind);
        var p = d.Proposal!;
        Assert.Equal("req1", p.Id);
        Assert.Equal(["a.", "Fri: b."], p.Lines);
        Assert.Equal(MessageProposalMode.Replace, p.Mode);
        Assert.Equal("why", p.Note);
        Assert.Equal(2, p.MessageLines.Count);
        Assert.Equal(T0, p.RequestedAt);
    }

    [Fact]
    public void RefusesWithReasons_OrDiscards()
    {
        var bad = MessageProposal.Decode(RequestJson(lines: new JsonArray("{blink} x")), T0);
        Assert.Equal(MessageDecodedKind.Refused, bad.Kind);
        Assert.Equal("req1", bad.Id);
        Assert.Contains("unknown effect", bad.Reasons[0]);

        var mode = MessageProposal.Decode(RequestJson(mode: "merge"), T0);
        Assert.Equal(MessageDecodedKind.Refused, mode.Kind);
        Assert.Equal(["mode must be add or replace"], mode.Reasons);

        var notAList = MessageProposal.Decode(RequestJson(lines: new JsonArray("a", 3)), T0);
        Assert.Equal(MessageDecodedKind.Refused, notAList.Kind);
        Assert.Contains("list", notAList.Reasons[0]);

        var note = MessageProposal.Decode(RequestJson(note: "a\nb"), T0);
        Assert.Equal(MessageDecodedKind.Refused, note.Kind);
        Assert.Contains("note", note.Reasons[0]);

        // Not a request, or too old: dropped without an answer.
        Assert.Equal(MessageDecodedKind.Discard, MessageProposal.Decode(Encoding.UTF8.GetBytes("{torn"), T0).Kind);
        Assert.Equal(MessageDecodedKind.Discard, MessageProposal.Decode(RequestJson(id: ""), T0).Kind);
        Assert.Equal(MessageDecodedKind.Discard, MessageProposal.Decode(RequestJson(at: T0.AddSeconds(-601)), T0).Kind);
        Assert.Equal(MessageDecodedKind.Proposal, MessageProposal.Decode(RequestJson(at: T0.AddSeconds(-599)), T0).Kind);
    }

    /// <summary>The server writes .NET-style stamps ("...:00.1234560+00:00").</summary>
    [Fact]
    public void ReadsTheServersStamp()
    {
        const string raw = """{"schema_version":1,"id":"x","requested_at":"2026-10-04T10:00:00.1234560+00:00","lines":["a."],"mode":"add","note":null}""";
        var now = DateTimeOffset.Parse("2026-10-04T10:01:00Z", CultureInfo.InvariantCulture);
        var d = MessageProposal.Decode(Encoding.UTF8.GetBytes(raw), now);
        Assert.Equal(MessageDecodedKind.Proposal, d.Kind);
        Assert.Null(d.Proposal!.Note);
    }

    [Fact]
    public void ParseDate_TakesIsoStampsOnly()
    {
        Assert.NotNull(MessageProposal.ParseDate("2026-10-04T10:00:00Z"));
        Assert.NotNull(MessageProposal.ParseDate("2026-10-04T10:00:00.123Z"));
        Assert.NotNull(MessageProposal.ParseDate("2026-10-04T10:00:00.123456789+02:00"));
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T08:00:00Z", CultureInfo.InvariantCulture), MessageProposal.ParseDate("2026-10-04T10:00:00+02:00"));
        Assert.Null(MessageProposal.ParseDate("2026-10-04T10:00:00"));   // no zone: not an Internet date-time
        Assert.Null(MessageProposal.ParseDate("yesterday"));
        Assert.Null(MessageProposal.ParseDate(""));
    }

    [Fact]
    public void RequestedAt_MissingOrBad_IsRefusedWithAReason()
    {
        var noStamp = MessageProposal.Decode(RequestJson(extra: o => o.Remove("requested_at")), T0);
        Assert.Equal(MessageDecodedKind.Refused, noStamp.Kind);
        Assert.Equal(["requested_at is missing or not a date"], noStamp.Reasons);
        var badStamp = MessageProposal.Decode(RequestJson(extra: o => o["requested_at"] = "soon"), T0);
        Assert.Equal(["requested_at is missing or not a date"], badStamp.Reasons);
    }

    [Fact]
    public void SchemaVersion_Other_Than1_IsRefused()
    {
        var d = MessageProposal.Decode(RequestJson(extra: o => o["schema_version"] = 2), T0);
        Assert.Equal(MessageDecodedKind.Refused, d.Kind);
        Assert.Equal(["this Sanduhr reads requests of schema 1"], d.Reasons);
        // Absent, or not a whole number, reads as 1.
        Assert.Equal(MessageDecodedKind.Proposal, MessageProposal.Decode(RequestJson(extra: o => o.Remove("schema_version")), T0).Kind);
        Assert.Equal(MessageDecodedKind.Proposal, MessageProposal.Decode(RequestJson(extra: o => o["schema_version"] = "2"), T0).Kind);
        Assert.Equal(MessageDecodedKind.Proposal, MessageProposal.Decode(RequestJson(extra: o => o["schema_version"] = 1.0), T0).Kind);
    }

    [Fact]
    public void AnIdOver64Characters_IsDiscarded()
    {
        Assert.Equal(MessageDecodedKind.Proposal, MessageProposal.Decode(RequestJson(id: new string('a', 64)), T0).Kind);
        Assert.Equal(MessageDecodedKind.Discard, MessageProposal.Decode(RequestJson(id: new string('a', 65)), T0).Kind);
    }

    [Fact]
    public void ModeAndNoteDefaults()
    {
        var d = MessageProposal.Decode(RequestJson(extra: o => { o.Remove("mode"); }), T0);
        Assert.Equal(MessageProposalMode.Add, d.Proposal!.Mode);
        // A note of blanks is no note; a long one is refused.
        Assert.Null(MessageProposal.Decode(RequestJson(note: "   "), T0).Proposal!.Note);
        var long300 = MessageProposal.Decode(RequestJson(note: new string('n', 300)), T0);
        Assert.Equal(MessageDecodedKind.Proposal, long300.Kind);
        var long301 = MessageProposal.Decode(RequestJson(note: new string('n', 301)), T0);
        Assert.Equal(MessageDecodedKind.Refused, long301.Kind);
    }

    [Fact]
    public void Add_AppendsAndSkipsRepeats()
    {
        const string existing = "# header\nkeep building.\nMon: one thing.\n\n\n";
        var a = MessageProposal.Apply(Proposal(["keep building.", "new one.", "# why", "new one."]), existing)!;
        Assert.Equal("# header\nkeep building.\nMon: one thing.\nnew one.\n# why\n", a.Text);
        Assert.Equal(1, a.Added);
        Assert.Equal(2, a.Skipped);
        var fresh = MessageProposal.Apply(Proposal(["a."]), "")!;
        Assert.Equal("a.\n", fresh.Text);
    }

    [Fact]
    public void Replace_KeepsTheHeaderOnly()
    {
        const string existing = "# Desk messages.\n#   Mon: text\n\nold one.\n# a note on old two\nold two.\n";
        var r = MessageProposal.Apply(Proposal(["Fri: {glow} new.", "another."], MessageProposalMode.Replace), existing)!;
        Assert.Equal("# Desk messages.\n#   Mon: text\n\nFri: {glow} new.\nanother.\n", r.Text);
        Assert.Equal(2, r.Added);
        var noHeader = MessageProposal.Apply(Proposal(["x."], MessageProposalMode.Replace), "old.\n")!;
        Assert.Equal("x.\n", noHeader.Text);
    }

    [Fact]
    public void Add_NeverGrowsPastTheCap()
    {
        string big = string.Join("\n", Enumerable.Range(0, MessageProposal.MaxFileLines).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        Assert.Null(MessageProposal.Apply(Proposal(["one more."]), big));
    }

    [Fact]
    public void Apply_ReadsCrlfAndOtherLineEndingsAsLines()
    {
        var a = MessageProposal.Apply(Proposal(["new."]), "# h\r\nold.\r\n\r\n")!;
        Assert.Equal("# h\nold.\nnew.\n", a.Text);
        // A lone CR or U+2028 also ends a line, as Swift's isNewline does.
        var b = MessageProposal.Apply(Proposal(["new."]), "old." + (char)0x2028 + "also.")!;
        Assert.Equal("old.\nalso.\nnew.\n", b.Text);
    }

    [Fact]
    public void ResultAndStateFiles()
    {
        var applied = new MessageApplied("", 2, 1);
        var root = Parse(MessageProposal.ResultJson("r", MessageProposalStatus.Applied, MessageProposalMode.Add, applied: applied, now: T0));
        Assert.Equal("r", (string?)root["id"]);
        Assert.IsType<string>((string?)root["completed_at"]);
        Assert.Equal("2026-10-04T16:56:40.000Z", (string?)root["completed_at"]);
        var result = (JsonObject)root["result"]!;
        Assert.Equal("applied", (string?)result["status"]);
        Assert.Equal("add", (string?)result["mode"]);
        Assert.Equal(2, (int?)result["lines_added"]);
        Assert.Equal(1, (int?)result["lines_skipped"]);
        Assert.False(result.ContainsKey("reasons"));
        var pending = Parse(MessageProposal.ResultJson("r", MessageProposalStatus.PendingApproval, MessageProposalMode.Replace));
        Assert.Equal("pending_approval", (string?)((JsonObject)pending["result"]!)["status"]);
        var rejected = Parse(MessageProposal.ResultJson("r", MessageProposalStatus.Rejected, null, ["why"], now: T0));
        var rr = (JsonObject)rejected["result"]!;
        Assert.Equal("rejected", (string?)rr["status"]);
        Assert.False(rr.ContainsKey("mode"));
        Assert.Equal("why", (string?)rr["reasons"]![0]);

        var pinned = Parse(MessageProposal.StateJson("{glow} pinned.", "hourly"));
        Assert.Equal(1, (int?)pinned["schema_version"]);
        Assert.True((bool?)pinned["pinned"]);
        Assert.Equal("{glow} pinned.", (string?)pinned["pinned_line"]);
        Assert.Equal("hourly", (string?)pinned["rotate"]);
        var free = Parse(MessageProposal.StateJson("", "weird"));
        Assert.False((bool?)free["pinned"]);
        Assert.Null(free["pinned_line"]);
        Assert.True(free.ContainsKey("pinned_line"));
        Assert.Equal("daily", (string?)free["rotate"]);
    }

    [Fact]
    public void StateFile_CarriesTheMixSwitchAndTheSpecialMode()
    {
        Assert.True((bool?)Parse(MessageProposal.StateJson(null, "daily", mixDaily: true))["mix_daily"]);
        Assert.False((bool?)Parse(MessageProposal.StateJson(null, "daily"))["mix_daily"]);
        var turns = Parse(MessageProposal.StateJson(null, "daily", specialMode: MessageSpecialMode.Turns, specialSeconds: 30));
        Assert.Equal("turns", (string?)turns["special_mode"]);
        Assert.Equal(30, (int?)turns["special_seconds"]);
        var plain = Parse(MessageProposal.StateJson(null, "daily"));
        Assert.Equal("stack", (string?)plain["special_mode"]);
        Assert.Equal(10, (int?)plain["special_seconds"]);
    }

    [Fact]
    public void JsonKeys_AreSorted_AndTheFilesAreCompact()
    {
        string state = Encoding.UTF8.GetString(MessageProposal.StateJson(null, "daily"));
        Assert.Equal("""{"mix_daily":false,"pinned":false,"pinned_line":null,"rotate":"daily","schema_version":1,"special_mode":"stack","special_seconds":10}""", state);
        string result = Encoding.UTF8.GetString(MessageProposal.ResultJson("r", MessageProposalStatus.Applied, MessageProposalMode.Add, applied: new MessageApplied("", 1, 0), now: T0));
        Assert.Equal("""{"completed_at":"2026-10-04T16:56:40.000Z","id":"r","result":{"lines_added":1,"lines_skipped":0,"mode":"add","status":"applied"}}""", result);
    }

    [Fact]
    public void NoticeTitle_CountsMessageLines()
    {
        Assert.Equal("Claude suggested 2 Desk messages", MessageSuggestionNotice.Title(Proposal(["a.", "# c", "b."])));
        Assert.Equal("Claude suggested 1 Desk message", MessageSuggestionNotice.Title(Proposal(["a."])));
    }
}
