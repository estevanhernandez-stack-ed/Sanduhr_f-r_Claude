import Foundation
import Testing
@testable import Sanduhr

/// Claude writes your Desk messages (item 54): the checks on a proposal (the MCP server's
/// wording), Add and Replace, the notice, and the request/result handoff on temp folders.
/// Made-up lines only; never the real messages.txt.

// MARK: - Checks, Add and Replace

@Suite("Message proposal checks")
struct MessageProposalCheckTests {
    @Test func goodLinesPass() {
        let good = ["keep building.", "Mon: one thing at a time.", "10-31: {ink:#ff7518,#6b2fa0} {write} boo.",
                    "{ink:#ff2a6d,#05d9e8} {glow} hello", "{size:0.5}{noglow} small.", "{SHIMMER} loud", "# a note",
                    "", "Note: a colon in a plain line.", "02-29: leap.", "{ink:fff} three digits.",
                    String(repeating: "x", count: 120), "hello {glow} mid-line braces are text", "ünïcödé ✨ fine."]
        #expect(MessageProposal.validate(good) == [])
    }

    /// The same cases and wording as the MCP server's tests (test_bad_lines_are_named).
    @Test func badLinesAreNamed() {
        let cases: [(String, String)] = [
            (String(repeating: "x", count: 121), "121 characters"),
            ("tab\there", "control character"),
            ("line\nbreak", "control character"),
            ("sep\u{2028}arator", "control character"),
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
        ]
        for (line, want) in cases {
            let reasons = MessageProposal.validate([line])
            #expect(reasons.count == 1, "\(line)")
            #expect(reasons.first?.hasPrefix("line 1 ") == true, "\(line)")
            #expect(reasons.first?.contains(want) == true, "\(line): \(reasons)")
        }
    }

    @Test func countsAndEmptyLists() {
        #expect(MessageProposal.validate(Array(repeating: "a", count: 61)).first?.contains("61 lines") == true)
        #expect(MessageProposal.validate(Array(repeating: "a", count: 60)) == [])
        #expect(MessageProposal.validate([]).first?.contains("list") == true)
        #expect(MessageProposal.validate(["# only", ""]).first?.contains("no message line") == true)
    }

    /// The handoff's names and limits match the MCP server's, read from its source.
    @Test func serverAgrees() throws {
        let server = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations/sanduhr_mcp.py")
        let source = try String(contentsOf: server, encoding: .utf8)
        #expect(source.contains(#"DESK_REQUEST_FILE = "\#(MessageProposal.requestFile)""#))
        #expect(source.contains(#"DESK_RESULT_FILE = "\#(MessageProposal.resultFile)""#))
        #expect(source.contains(#"DESK_STATE_FILE = "\#(MessageProposal.stateFile)""#))
        #expect(source.contains("DESK_MAX_LINES = \(MessageProposal.maxLines)"))
        #expect(source.contains("DESK_MAX_LINE_CHARS = \(MessageProposal.maxLineChars)"))
        #expect(source.contains("DESK_MAX_NOTE_CHARS = \(MessageProposal.maxNoteChars)"))
        #expect(source.contains(#"EFFECT_NAMES = "\#(MessageMarkup.effectNames)""#))
        #expect(source.contains(#"FONT_STYLES = "\#(LetterStyle.tagNames)""#))
        #expect(source.contains("SWEEP_PERIOD = (\(Int(MessageMarkup.sweepPeriodRange.lowerBound)), \(Int(MessageMarkup.sweepPeriodRange.upperBound)))"))
    }
}

private let t0 = Date(timeIntervalSince1970: 1_791_133_000)

private func proposal(_ lines: [String], _ mode: MessageProposal.Mode = .add, note: String? = nil) -> MessageProposal {
    MessageProposal(id: "abc", requestedAt: t0, lines: lines, mode: mode, note: note)
}

private func requestJSON(id: String = "req1", at: Date = t0, lines: [Any] = ["{glow} hi."], mode: String = "add",
                         note: Any? = nil, extra: [String: Any] = [:]) -> Data {
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    var root: [String: Any] = ["schema_version": 1, "id": id, "requested_at": f.string(from: at),
                               "lines": lines, "mode": mode]
    if let note { root["note"] = note }
    root.merge(extra) { $1 }
    return try! JSONSerialization.data(withJSONObject: root)
}

@Suite("Message proposal requests and apply")
struct MessageProposalApplyTests {
    @Test func decodesAGoodRequest() {
        let d = MessageProposal.decode(requestJSON(lines: ["  a.  ", "Fri: b."], mode: "replace", note: " why "), now: t0)
        guard case .proposal(let p) = d else { Issue.record("\(d)"); return }
        #expect(p.id == "req1")
        #expect(p.lines == ["a.", "Fri: b."])
        #expect(p.mode == .replace)
        #expect(p.note == "why")
        #expect(p.messageLines.count == 2)
    }

    @Test func refusesWithReasonsOrDiscards() {
        if case .refused(let id, let reasons) = MessageProposal.decode(requestJSON(lines: ["{blink} x"]), now: t0) {
            #expect(id == "req1")
            #expect(reasons.first?.contains("unknown effect") == true)
        } else { Issue.record("expected a refusal") }
        if case .refused(_, let reasons) = MessageProposal.decode(requestJSON(mode: "merge"), now: t0) {
            #expect(reasons == ["mode must be add or replace"])
        } else { Issue.record("expected a refusal") }
        if case .refused(_, let reasons) = MessageProposal.decode(requestJSON(lines: ["a", 3]), now: t0) {
            #expect(reasons.first?.contains("list") == true)
        } else { Issue.record("expected a refusal") }
        if case .refused(_, let reasons) = MessageProposal.decode(requestJSON(note: "a\nb"), now: t0) {
            #expect(reasons.first?.contains("note") == true)
        } else { Issue.record("expected a refusal") }
        // Not a request, or too old: dropped without an answer.
        #expect(MessageProposal.decode(Data("{torn".utf8), now: t0) == .discard)
        #expect(MessageProposal.decode(requestJSON(id: ""), now: t0) == .discard)
        #expect(MessageProposal.decode(requestJSON(at: t0.addingTimeInterval(-601)), now: t0) == .discard)
        if case .proposal = MessageProposal.decode(requestJSON(at: t0.addingTimeInterval(-599)), now: t0) {} else {
            Issue.record("a request inside ten minutes is read")
        }
    }

    /// The server writes .NET-style stamps ("…:00.1234560+00:00").
    @Test func readsTheServersStamp() {
        let raw = #"{"schema_version":1,"id":"x","requested_at":"2026-10-04T10:00:00.1234560+00:00","lines":["a."],"mode":"add","note":null}"#
        let now = ISO8601DateFormatter().date(from: "2026-10-04T10:01:00Z")!
        if case .proposal(let p) = MessageProposal.decode(Data(raw.utf8), now: now) {
            #expect(p.note == nil)
        } else { Issue.record("expected a proposal") }
    }

    @Test func addAppendsAndSkipsRepeats() throws {
        let existing = "# header\nkeep building.\nMon: one thing.\n\n\n"
        let a = try #require(MessageProposal.apply(proposal(["keep building.", "new one.", "# why", "new one."]), to: existing))
        #expect(a.text == "# header\nkeep building.\nMon: one thing.\nnew one.\n# why\n")
        #expect(a.added == 1)
        #expect(a.skipped == 2)
        let fresh = try #require(MessageProposal.apply(proposal(["a."]), to: ""))
        #expect(fresh.text == "a.\n")
    }

    @Test func replaceKeepsTheHeaderOnly() throws {
        let existing = "# Desk messages.\n#   Mon: text\n\nold one.\n# a note on old two\nold two.\n"
        let r = try #require(MessageProposal.apply(proposal(["Fri: {glow} new.", "another."], .replace), to: existing))
        #expect(r.text == "# Desk messages.\n#   Mon: text\n\nFri: {glow} new.\nanother.\n")
        #expect(r.added == 2)
        let noHeader = try #require(MessageProposal.apply(proposal(["x."], .replace), to: "old.\n"))
        #expect(noHeader.text == "x.\n")
    }

    @Test func addNeverGrowsPastTheCap() {
        let big = Array(repeating: "line", count: MessageProposal.maxFileLines).enumerated().map { "\($0.offset)" }
        #expect(MessageProposal.apply(proposal(["one more."]), to: big.joined(separator: "\n")) == nil)
    }

    @Test func resultAndStateFiles() throws {
        let applied = MessageProposal.Applied(text: "", added: 2, skipped: 1)
        let data = MessageProposal.resultJSON(id: "r", status: .applied, mode: .add, applied: applied, now: t0)
        let root = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(root["id"] as? String == "r")
        #expect(root["completed_at"] is String)
        let result = try #require(root["result"] as? [String: Any])
        #expect(result["status"] as? String == "applied")
        #expect(result["mode"] as? String == "add")
        #expect(result["lines_added"] as? Int == 2)
        #expect(result["lines_skipped"] as? Int == 1)
        #expect(result["reasons"] == nil)
        let pending = try #require(try JSONSerialization.jsonObject(
            with: MessageProposal.resultJSON(id: "r", status: .pendingApproval, mode: .replace)) as? [String: Any])
        #expect((pending["result"] as? [String: Any])?["status"] as? String == "pending_approval")

        let pinned = try #require(try JSONSerialization.jsonObject(
            with: MessageProposal.stateJSON(pinned: "{glow} pinned.", rotate: "hourly")) as? [String: Any])
        #expect(pinned["schema_version"] as? Int == 1)
        #expect(pinned["pinned"] as? Bool == true)
        #expect(pinned["pinned_line"] as? String == "{glow} pinned.")
        #expect(pinned["rotate"] as? String == "hourly")
        let free = try #require(try JSONSerialization.jsonObject(
            with: MessageProposal.stateJSON(pinned: "", rotate: "weird")) as? [String: Any])
        #expect(free["pinned"] as? Bool == false)
        #expect(free["pinned_line"] is NSNull)
        #expect(free["rotate"] as? String == "daily")
    }

    @Test func noticeFollowsTheAlertSettings() {
        var s = AlertSettings()
        let noon = Date(timeIntervalSince1970: 1_785_067_200)
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        #expect(!MessageSuggestionNotice.banner(s, deskRunning: true, now: noon, calendar: cal))   // alerts off: badge only
        s.enabled = true
        #expect(MessageSuggestionNotice.banner(s, deskRunning: true, now: noon, calendar: cal))
        s.delivery = .desk
        #expect(!MessageSuggestionNotice.banner(s, deskRunning: true, now: noon, calendar: cal))
        #expect(MessageSuggestionNotice.banner(s, deskRunning: false, now: noon, calendar: cal))
        s.delivery = .both
        s.quietEnabled = true
        s.quietStart = 11 * 60
        s.quietEnd = 13 * 60
        #expect(!MessageSuggestionNotice.banner(s, deskRunning: true, now: noon, calendar: cal))
        #expect(MessageSuggestionNotice.title(proposal(["a.", "# c", "b."])) == "Claude suggested 2 Desk messages")
        #expect(MessageSuggestionNotice.title(proposal(["a."])) == "Claude suggested 1 Desk message")
    }
}

// MARK: - The handoff, on temp folders

@MainActor
@Suite("Desk message handoff")
struct DeskMessageHandoffTests {
    final class Folder {
        let root: URL
        let paths: DeskMessageHandoff.Paths
        init() {
            root = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-msg-\(UUID().uuidString)")
            paths = DeskMessageHandoff.Paths(support: root.appendingPathComponent("Sanduhr"),
                                             messages: root.appendingPathComponent("Desk/messages.txt"))
            try? FileManager.default.createDirectory(at: paths.support, withIntermediateDirectories: true)
            try? FileManager.default.createDirectory(at: paths.messages.deletingLastPathComponent(), withIntermediateDirectories: true)
        }
        deinit { try? FileManager.default.removeItem(at: root) }

        func messages() -> String? { try? String(contentsOf: paths.messages, encoding: .utf8) }
        func result() -> [String: Any]? {
            guard let data = try? Data(contentsOf: paths.result),
                  let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
            return root
        }
        func status() -> String? { (result()?["result"] as? [String: Any])?["status"] as? String }
        func drop(_ data: Data) { try? data.write(to: paths.request) }
    }

    private func handoff(_ f: Folder, direct: Bool = false) -> DeskMessageHandoff {
        DeskMessageHandoff(paths: f.paths, direct: { direct }, now: { t0 })
    }

    @Test func waitsForApprovalThenAdds() throws {
        let f = Folder()
        try "keep building.\n".write(to: f.paths.messages, atomically: true, encoding: .utf8)
        let h = handoff(f)
        var suggested: [String] = [], applied = 0
        h.onSuggestion = { suggested.append($0.id) }
        h.onApplied = { applied += 1 }
        f.drop(requestJSON(lines: ["{write} new."]))
        h.check()
        #expect(!FileManager.default.fileExists(atPath: f.paths.request.path))   // read once, removed
        #expect(h.pending?.id == "req1")
        #expect(suggested == ["req1"])
        #expect(f.status() == "pending_approval")
        #expect(f.result()?["id"] as? String == "req1")
        #expect(f.messages() == "keep building.\n")   // nothing changes before Add
        h.approve()
        #expect(h.pending == nil)
        #expect(f.messages() == "keep building.\n{write} new.\n")
        #expect(try String(contentsOf: f.paths.backup, encoding: .utf8) == "keep building.\n")
        #expect(f.status() == "applied")
        #expect(applied == 1)
        #expect(h.revision == 1)
        let perms = try FileManager.default.attributesOfItem(atPath: f.paths.result.path)[.posixPermissions] as? Int
        #expect(perms == 0o600)
    }

    @Test func dismissLeavesTheListAlone() {
        let f = Folder()
        let h = handoff(f)
        var decided = 0
        h.onDecided = { _ in decided += 1 }
        h.receive(requestJSON(lines: ["a."]))
        h.dismiss()
        #expect(h.pending == nil)
        #expect(f.messages() == nil)
        #expect(f.status() == "rejected")
        #expect(((f.result()?["result"] as? [String: Any])?["reasons"] as? [String]) == ["dismissed by the user"])
        #expect(decided == 1)
    }

    @Test func directAppliesAtOnce() {
        let f = Folder()
        let h = handoff(f, direct: true)
        var suggested = 0
        h.onSuggestion = { _ in suggested += 1 }
        h.receive(requestJSON(lines: ["Fri: {shimmer} showtime."], mode: "replace"))
        #expect(h.pending == nil)
        #expect(suggested == 0)
        #expect(f.messages() == "Fri: {shimmer} showtime.\n")
        #expect(!FileManager.default.fileExists(atPath: f.paths.backup.path))   // there was no list before
        #expect(f.status() == "applied")
    }

    @Test func refusedAndExpiredRequests() {
        let f = Folder()
        let h = handoff(f, direct: true)
        h.receive(requestJSON(lines: ["{blink} x"]))
        #expect(f.status() == "rejected")
        #expect(f.messages() == nil)
        try? FileManager.default.removeItem(at: f.paths.result)
        h.receive(requestJSON(at: t0.addingTimeInterval(-3600)))
        #expect(f.result() == nil)
        #expect(f.messages() == nil)
    }

    @Test func aNewerSuggestionReplacesTheWaitingOne() {
        let f = Folder()
        let h = handoff(f)
        var decided: [String] = []
        h.onDecided = { decided.append($0.id) }
        h.receive(requestJSON(id: "one"))
        h.receive(requestJSON(id: "two"))
        #expect(h.pending?.id == "two")
        #expect(decided == ["one"])
    }

    @Test func aListThatIsNotUTF8IsLeftAlone() throws {
        let f = Folder()
        try Data([0xff, 0xfe, 0x41]).write(to: f.paths.messages)
        let h = handoff(f, direct: true)
        h.receive(requestJSON())
        #expect(f.status() == "rejected")
        #expect(try Data(contentsOf: f.paths.messages) == Data([0xff, 0xfe, 0x41]))
    }

    @Test func stateIsWrittenOnlyWhenItChanges() throws {
        let f = Folder()
        let h = handoff(f)
        h.writeState(pinned: nil, rotate: "daily")
        let first = try Data(contentsOf: f.paths.state)
        try FileManager.default.removeItem(at: f.paths.state)
        h.writeState(pinned: "", rotate: "daily")   // same state: not rewritten
        #expect(!FileManager.default.fileExists(atPath: f.paths.state.path))
        h.writeState(pinned: "hi.", rotate: "daily")
        #expect(try Data(contentsOf: f.paths.state) != first)
    }
}
