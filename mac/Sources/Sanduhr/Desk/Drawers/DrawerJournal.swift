import Foundation

/// The move journal (docs/mac-drawers-setup-spec.md, section 5). Pure: it encodes, decodes and
/// trims; the file, its 0600 mode and the atomic write belong to the mover. The JSON is Windows'
/// shape (`DrawerJournal.cs`) so a config pack can carry it between the two:
/// `{"entries":[{"id","at","source","agent"?,"moves":[{"from","to","folder","written"}]}]}`.

/// Where a move stands. Windows' `written` is the file's last write time (null for a folder). The
/// Mac also needs "not moved yet" for the crash-safe entry, so the one key carries three shapes:
/// `false` is still to move, an ISO time is moved (with the date Undo checks), `true` is moved with
/// no date (a folder). Windows reads `false` and `true` as "no date", which only skips its date check.
enum JournalWritten: Equatable, Sendable {
    case pending
    case done(modified: Date?)
}

/// One move as it landed. `modified` is the file's modification date when it moved, so Undo can
/// tell it changed since; a folder carries none, since its date changes with what is put in it.
struct JournalMove: Equatable, Sendable, Codable {
    var from: String
    var to: String
    var isFolder: Bool
    var written: JournalWritten

    init(from: String, to: String, isFolder: Bool = false, written: JournalWritten) {
        self.from = from
        self.to = to
        self.isFolder = isFolder
        self.written = written
    }

    private enum Keys: String, CodingKey { case from, to, folder, written }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        from = try c.decode(String.self, forKey: .from)
        to = try c.decode(String.self, forKey: .to)
        isFolder = (try? c.decode(Bool.self, forKey: .folder)) ?? false
        if let flag = try? c.decode(Bool.self, forKey: .written) {
            written = flag ? .done(modified: nil) : .pending
        } else if let text = try? c.decode(String.self, forKey: .written), let date = JournalTime.parse(text) {
            written = .done(modified: date)
        } else {
            written = .done(modified: nil)
        }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        try c.encode(from, forKey: .from)
        try c.encode(to, forKey: .to)
        try c.encode(isFolder, forKey: .folder)
        switch written {
        case .pending: try c.encode(false, forKey: .written)
        case .done(let date?): try c.encode(JournalTime.format(date), forKey: .written)
        case .done(nil): try c.encode(true, forKey: .written)
        }
    }

    var isWritten: Bool { if case .done = written { return true } else { return false } }
}

/// One applied plan, or one hand move by the user, as a single undoable entry.
struct JournalEntry: Equatable, Sendable, Codable {
    var id: String
    var at: Date
    var source: DrawerPlanSource
    /// Windows' agent name, kept so a Windows journal round-trips. The Mac never sets it.
    var agent: String?
    var moves: [JournalMove]

    init(id: String, at: Date, source: DrawerPlanSource, agent: String? = nil, moves: [JournalMove]) {
        self.id = id
        self.at = at
        self.source = source
        self.agent = agent
        self.moves = moves
    }

    private enum Keys: String, CodingKey { case id, at, source, agent, moves }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        id = try c.decode(String.self, forKey: .id)
        guard let when = JournalTime.parse(try c.decode(String.self, forKey: .at)) else {
            throw DecodingError.dataCorruptedError(forKey: .at, in: c, debugDescription: "not a time")
        }
        at = when
        source = try c.decode(DrawerPlanSource.self, forKey: .source)
        agent = try? c.decode(String.self, forKey: .agent)
        moves = try c.decode([LossyMove].self, forKey: .moves).compactMap(\.value)
        if moves.isEmpty { throw DecodingError.dataCorruptedError(forKey: .moves, in: c, debugDescription: "no moves") }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        try c.encode(id, forKey: .id)
        try c.encode(JournalTime.format(at), forKey: .at)
        try c.encode(source, forKey: .source)
        try c.encodeIfPresent(agent, forKey: .agent)
        try c.encode(moves, forKey: .moves)
    }

    /// Moves that left the starting place. A move still `pending` may or may not have happened,
    /// so Undo leaves it alone.
    var writtenMoves: [JournalMove] { moves.filter(\.isWritten) }

    /// A crash left this entry open: some move never got its flag.
    var isOpen: Bool { moves.contains { !$0.isWritten } }
}

/// What the MCP may read of the journal: when, who and how many. Never a path.
struct JournalSummaryLine: Equatable, Sendable {
    var at: Date
    var source: DrawerPlanSource
    var files: Int
}

/// The move journal. Immutable: `adding` returns the trimmed journal to save.
struct DrawerJournal: Equatable, Sendable, Codable {
    static let maxRuns = 50
    static let keepUserMoves: TimeInterval = 30 * 86_400

    private(set) var entries: [JournalEntry]

    init(entries: [JournalEntry] = []) { self.entries = entries }

    /// The journal with `entry` added, user moves older than 30 days dropped and only the newest 50
    /// rules runs kept.
    func adding(_ entry: JournalEntry, now: Date) -> DrawerJournal {
        var all = (entries + [entry]).filter { $0.source != .user || now.timeIntervalSince($0.at) <= Self.keepUserMoves }
        var runs = all.filter { $0.source != .user }.count
        all.removeAll { e in
            guard e.source != .user, runs > Self.maxRuns else { return false }
            runs -= 1
            return true
        }
        return DrawerJournal(entries: all)
    }

    /// Replaces the entry with the same id (the open entry as its moves flip), or appends it.
    func updating(_ entry: JournalEntry) -> DrawerJournal {
        var all = entries
        if let i = all.firstIndex(where: { $0.id == entry.id }) { all[i] = entry } else { all.append(entry) }
        return DrawerJournal(entries: all)
    }

    /// The journal without one entry (undone, or its files all gone). An unknown id changes nothing.
    func without(_ id: String) -> DrawerJournal { DrawerJournal(entries: entries.filter { $0.id != id }) }

    /// Entries a crash left open, found on load. Each is offered as Undo for its written moves.
    var openEntries: [JournalEntry] { entries.filter(\.isOpen) }

    /// An entry as Undo sees it: only the moves that happened.
    static func recovered(_ entry: JournalEntry) -> JournalEntry {
        var e = entry
        e.moves = entry.writtenMoves
        return e
    }

    func summary() -> [JournalSummaryLine] {
        entries.map { JournalSummaryLine(at: $0.at, source: $0.source, files: $0.writtenMoves.count) }
    }

    // MARK: Codable (the file's shape)

    private enum Keys: String, CodingKey { case entries }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        entries = try c.decode([LossyEntry].self, forKey: .entries).compactMap(\.value)
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        try c.encode(entries, forKey: .entries)
    }

    func encoded() -> String {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return (try? enc.encode(self)).flatMap { String(data: $0, encoding: .utf8) } ?? #"{"entries":[]}"#
    }

    /// Reads a saved journal. An unreadable entry or move is dropped on its own; a file that is not
    /// a journal reads as empty.
    static func decode(_ json: String?) -> DrawerJournal {
        guard let json, let data = json.data(using: .utf8),
              let j = try? JSONDecoder().decode(DrawerJournal.self, from: data) else { return DrawerJournal() }
        return j
    }
}

/// Decodes one element or nothing, so one bad entry doesn't lose the rest.
private struct LossyEntry: Decodable {
    let value: JournalEntry?
    init(from decoder: Decoder) throws { value = try? JournalEntry(from: decoder) }
}

private struct LossyMove: Decodable {
    let value: JournalMove?
    init(from decoder: Decoder) throws { value = try? JournalMove(from: decoder) }
}

/// Windows' time format: ISO 8601 in UTC with seven fractional digits ("2026-11-01T12:00:00.0000000Z").
/// Reads any fraction length and an offset too, since that is what .NET's round-trip format allows.
enum JournalTime {
    static func format(_ date: Date) -> String {
        var secs = date.timeIntervalSince1970.rounded(.down)
        var ticks = Int(((date.timeIntervalSince1970 - secs) * 10_000_000).rounded())
        if ticks >= 10_000_000 { secs += 1; ticks -= 10_000_000 }
        let base = whole.string(from: Date(timeIntervalSince1970: secs)).replacingOccurrences(of: "Z", with: "")
        return base + "." + String(format: "%07d", ticks) + "Z"
    }

    static func parse(_ text: String) -> Date? {
        var s = text
        var fraction = 0.0
        if let dot = s.firstIndex(of: ".") {
            let digits = s[s.index(after: dot)...].prefix { $0.isNumber }
            guard !digits.isEmpty, let f = Double("0." + digits) else { return nil }
            fraction = f
            s.removeSubrange(dot..<s.index(dot, offsetBy: 1 + digits.count))
        }
        guard let base = whole.date(from: s) else { return nil }
        return base.addingTimeInterval(fraction)
    }

    private static let whole: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()
}
