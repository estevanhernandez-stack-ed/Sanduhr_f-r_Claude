import Foundation

/// Claude's suggested Desk messages (item 54), the pure half: the request the MCP server's
/// `propose_desk_messages` writes, the checks the app runs again (it is the authority), what Add
/// does to messages.txt, and the result the server waits for. DeskMessageHandoff does the files.
///
/// The server mirrors the file names, the limits and the reasons' wording
/// (`mac/integrations/sanduhr_mcp.py`); a test on each side pins them.
struct MessageProposal: Equatable, Identifiable {
    enum Mode: String, Equatable { case add, replace }

    let id: String
    let requestedAt: Date
    let lines: [String]
    let mode: Mode
    let note: String?

    static let requestFile = "desk-messages-request.json"
    static let resultFile = "desk-messages-result.json"
    static let stateFile = "desk-messages-state.json"

    static let maxLines = 60
    static let maxLineChars = 120
    static let maxNoteChars = 300
    /// A request is interactive: one older than this (Sanduhr was not running) is dropped.
    static let maxAge: TimeInterval = 600
    /// Add never grows messages.txt past this many lines.
    static let maxFileLines = 1000

    /// The lines that show on the Desk (not blank, not a comment).
    var messageLines: [String] { lines.filter(MessageProposal.isMessageLine) }

    static func isMessageLine(_ line: String) -> Bool {
        let t = line.trimmingCharacters(in: .whitespaces)
        return !t.isEmpty && !t.hasPrefix("#")
    }

    // MARK: - Reading the request

    enum Decoded: Equatable {
        case proposal(MessageProposal)
        /// Readable enough to answer: the result names the id.
        case refused(id: String, reasons: [String])
        /// Not a request (unreadable, no id) or too old: dropped without an answer.
        case discard
    }

    static func decode(_ data: Data, now: Date) -> Decoded {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let id = root["id"] as? String, !id.isEmpty, id.count <= 64 else { return .discard }
        guard let stamp = root["requested_at"] as? String, let at = parseDate(stamp) else {
            return .refused(id: id, reasons: ["requested_at is missing or not a date"])
        }
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard (root["schema_version"] as? Int ?? 1) == 1 else {
            return .refused(id: id, reasons: ["this Sanduhr reads requests of schema 1"])
        }
        var reasons: [String] = []
        let modeText = root["mode"] as? String ?? "add"
        let mode = Mode(rawValue: modeText)
        if mode == nil { reasons.append("mode must be add or replace") }
        var note: String?
        if let raw = root["note"], !(raw is NSNull) {
            if let s = raw as? String, s.unicodeScalars.count <= maxNoteChars, !s.unicodeScalars.contains(where: isControl) {
                let t = s.trimmingCharacters(in: .whitespaces)
                note = t.isEmpty ? nil : t
            } else {
                reasons.append("note must be one line of at most \(maxNoteChars) characters")
            }
        }
        let rawLines = root["lines"] as? [Any] ?? []
        let strings = rawLines.compactMap { $0 as? String }
        if strings.count != rawLines.count {
            reasons.append("lines must be a list of 1 to \(maxLines) strings")
        } else {
            reasons += validate(strings)
        }
        guard reasons.isEmpty, let mode else { return .refused(id: id, reasons: Array(reasons.prefix(20))) }
        let lines = strings.map { $0.trimmingCharacters(in: .whitespaces) }
        return .proposal(MessageProposal(id: id, requestedAt: at, lines: lines, mode: mode, note: note))
    }

    private static func parseDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        if let d = f.date(from: s) { return d }
        // .NET-style seven fraction digits ("…:00.1234560+00:00"): keep three.
        if let r = s.range(of: #"\.(\d{3})\d+"#, options: .regularExpression) {
            var t = s
            t.replaceSubrange(r, with: "." + s[r].dropFirst().prefix(3))
            f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            return f.date(from: t)
        }
        return nil
    }

    // MARK: - The checks

    /// Why the lines are refused, [] when they may be applied. Same rules and wording as the server.
    static func validate(_ lines: [String]) -> [String] {
        guard !lines.isEmpty else { return ["lines must be a list of 1 to \(maxLines) strings"] }
        var reasons: [String] = []
        if lines.count > maxLines { reasons.append("\(lines.count) lines; the limit is \(maxLines)") }
        for (i, line) in lines.prefix(maxLines * 2).enumerated() {
            if let why = check(line) { reasons.append("line \(i + 1) \(why)") }
        }
        if reasons.isEmpty, !lines.contains(where: isMessageLine) {
            reasons.append("no message line: every line is blank or a # comment")
        }
        return Array(reasons.prefix(20))
    }

    static var weekdays: [String] { MessageEngine.weekdays }
    private static let weekdayLookalikes: Set<String> = Set(weekdays.map { $0.lowercased() }).union([
        "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "tues", "thur", "thurs"])
    private static let daysInMonth = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

    /// U+0000–001F, U+007F–009F and the Unicode line and paragraph separators.
    static func isControl(_ s: Unicode.Scalar) -> Bool {
        s.value < 0x20 || (0x7F...0x9F).contains(s.value) || s.value == 0x2028 || s.value == 0x2029
    }

    /// One line: nil when good, else why not (the caller adds "line N").
    static func check(_ line: String) -> String? {
        if line.unicodeScalars.contains(where: isControl) { return "has a control character or a line break" }
        let text = line.trimmingCharacters(in: .whitespaces)
        let count = text.unicodeScalars.count
        if count > maxLineChars { return "is \(count) characters; the limit is \(maxLineChars)" }
        if text.isEmpty || text.hasPrefix("#") { return nil }
        if let colon = text.firstIndex(of: ":") {
            let tag = text[..<colon].trimmingCharacters(in: .whitespaces)
            if tag.range(of: #"^\d{1,2}-\d{1,2}$"#, options: .regularExpression) != nil {
                let parts = tag.split(separator: "-").compactMap { Int($0) }
                let valid = tag.count == 5 && parts.count == 2 && (1...12).contains(parts[0])
                    && (1...daysInMonth[max(0, min(11, parts[0] - 1))]).contains(parts[1])
                if !valid { return "starts with '\(tag):', which is not a date; write MM-DD, like 10-31" }
            } else if weekdayLookalikes.contains(tag.lowercased()), !weekdays.contains(tag) {
                return "starts with '\(tag.prefix(12)):'; write the day as Mon, Tue, Wed, Thu, Fri, Sat or Sun"
            }
        }
        let split = MessageEngine.splitPrefix(text)
        if split.kind != .plain, split.body.isEmpty { return "has a prefix but no text" }
        if case .failure(let failure) = MessageMarkup.parseStrict(split.body) { return failure.reason }
        return nil
    }

    // MARK: - Applying

    struct Applied: Equatable {
        /// messages.txt as it will be written.
        let text: String
        let added: Int
        let skipped: Int
    }

    /// What Add (or the direct opt-in) makes of `existing`, or nil when the list would pass
    /// `maxFileLines`.
    ///
    /// add: the lines are appended; a message line already in the list (or twice in the
    /// proposal) is skipped. replace: the comment block at the top of the file (the `#` and blank
    /// lines before the first message) stays, as the notes on how the file works; every other
    /// line, comments among the old messages included, is replaced. Either way the previous file
    /// is kept as messages.txt.previous by the caller.
    static func apply(_ p: MessageProposal, to existing: String) -> Applied? {
        var old = existing.split(omittingEmptySubsequences: false, whereSeparator: \.isNewline).map(String.init)
        while let last = old.last, last.trimmingCharacters(in: .whitespaces).isEmpty { old.removeLast() }
        var out: [String]
        var added = 0, skipped = 0
        switch p.mode {
        case .add:
            out = old
            var seen = Set(old.filter(isMessageLine).map { $0.trimmingCharacters(in: .whitespaces) })
            for line in p.lines {
                if isMessageLine(line) {
                    if seen.contains(line) { skipped += 1; continue }
                    seen.insert(line)
                    added += 1
                }
                out.append(line)
            }
        case .replace:
            let header = old.prefix { !isMessageLine($0) }
            out = Array(header)
            while let last = out.last, last.trimmingCharacters(in: .whitespaces).isEmpty { out.removeLast() }
            if !out.isEmpty { out.append("") }
            out += p.lines
            added = p.messageLines.count
        }
        while let last = out.last, last.trimmingCharacters(in: .whitespaces).isEmpty { out.removeLast() }
        guard out.count <= maxFileLines else { return nil }
        return Applied(text: out.joined(separator: "\n") + "\n", added: added, skipped: skipped)
    }

    // MARK: - The result

    enum Status: String { case applied, pendingApproval = "pending_approval", rejected }

    /// The result file's contents: `{id, completed_at, result: {status, mode, reasons?,
    /// lines_added?, lines_skipped?}}`.
    static func resultJSON(id: String, status: Status, mode: Mode?, reasons: [String] = [],
                           applied: Applied? = nil, now: Date = Date()) -> Data {
        var result: [String: Any] = ["status": status.rawValue]
        if let mode { result["mode"] = mode.rawValue }
        if !reasons.isEmpty { result["reasons"] = reasons }
        if let applied {
            result["lines_added"] = applied.added
            result["lines_skipped"] = applied.skipped
        }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let root: [String: Any] = ["id": id, "completed_at": f.string(from: now), "result": result]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])) ?? Data()
    }

    /// desk-messages-state.json: what get_desk_messages reports about the user's settings. The
    /// pinned line is written only while one is pinned; it is on the desktop already.
    static func stateJSON(pinned: String?, rotate: String) -> Data {
        let line = pinned.flatMap { $0.isEmpty ? nil : $0 }
        let root: [String: Any] = [
            "schema_version": 1,
            "pinned": line != nil,
            "pinned_line": line ?? NSNull(),
            "rotate": rotate == "hourly" ? "hourly" : "daily",
        ]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])) ?? Data()
    }
}

/// Whether a suggestion also gets a notification banner: only while Sanduhr's alerts are on, as
/// an alert would (the delivery choice, quiet hours), and never with a sound. Otherwise the badge
/// on Settings, Message is the only sign.
enum MessageSuggestionNotice {
    static func banner(_ s: AlertSettings, deskRunning: Bool, now: Date, calendar: Calendar = .current) -> Bool {
        guard s.enabled else { return false }
        let quiet = s.quietEnabled && AlertRules.isQuiet(now, start: s.quietStart, end: s.quietEnd, calendar: calendar)
        return AlertRules.route(s.delivery, deskRunning: deskRunning, quiet: quiet).banner
    }

    static func title(_ p: MessageProposal) -> String {
        let n = p.messageLines.count
        return "Claude suggested \(n) Desk message\(n == 1 ? "" : "s")"
    }
}
