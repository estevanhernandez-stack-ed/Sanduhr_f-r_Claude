import Foundation

/// Settings, Message's line editor (item 69), the pure half: messages.txt read into rows and
/// written back. A message line the editor can represent becomes a styled row (when it shows, its
/// text, its look); one it can't (an unknown or bad tag, two motions on one line, a date that
/// isn't one) stays a raw row, kept verbatim; comments and blank lines keep their place. Each row
/// remembers the line it was read from and writes that line, byte for byte, until it is changed,
/// so an unchanged file saves back exactly and a changed row rewrites only its own line.
///
/// The controls emit the existing tags only (MessageEffects.swift); there is no new grammar.

/// When a line shows: the `Mon:` and `10-31:` prefixes (MessageEngine).
enum MessageWhen: Hashable {
    case everyDay
    /// "Mon" to "Sun", as MessageEngine.weekdays writes them.
    case weekday(String)
    case date(month: Int, day: Int)

    /// The When menu's order: Monday first.
    static let weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
    static let weekdayNames = ["Mon": "Monday", "Tue": "Tuesday", "Wed": "Wednesday", "Thu": "Thursday",
                               "Fri": "Friday", "Sat": "Saturday", "Sun": "Sunday"]
    static let monthNames = ["January", "February", "March", "April", "May", "June", "July",
                             "August", "September", "October", "November", "December"]

    /// What goes before the text: "", "Mon: " or "10-31: ".
    var prefix: String {
        switch self {
        case .everyDay: ""
        case .weekday(let day): "\(day): "
        case .date(let month, let day): String(format: "%02d-%02d: ", month, day)
        }
    }

    /// "Every day", "Fridays", "October 31".
    var label: String {
        switch self {
        case .everyDay: "Every day"
        case .weekday(let day): "\(Self.weekdayNames[day] ?? day)s"
        case .date(let month, let day): "\(Self.monthNames[max(0, min(11, month - 1))]) \(day)"
        }
    }

    /// Days in a month, February with the 29th (a leap day's line is a fine line).
    static func days(inMonth month: Int) -> Int {
        switch month {
        case 2: 29
        case 4, 6, 9, 11: 30
        default: 31
        }
    }

    /// "10-31" as a date, nil for anything that isn't one ("13-01", "02-30").
    static func date(_ tag: String) -> MessageWhen? {
        let parts = tag.split(separator: "-")
        guard parts.count == 2, let m = Int(parts[0]), let d = Int(parts[1]),
              (1...12).contains(m), (1...days(inMonth: m)).contains(d) else { return nil }
        return .date(month: m, day: d)
    }
}

/// The Motion choice: one per line.
enum MessageMotionKind: String, CaseIterable, Hashable {
    case none, write, shimmer, sweep

    var title: String {
        switch self {
        case .none: "None"
        case .write: "Write in"
        case .shimmer: "Shimmer"
        case .sweep: "Sweep"
        }
    }
}

/// A line's look, as the controls set it. Every field maps to one existing tag.
struct MessageLook: Hashable {
    /// Hex colors without "#", 1 to 4; nil keeps Settings, Desk, Look's message color.
    var ink: [String]?
    /// nil is as the Desk (the Look page's glow switch).
    var glow: Bool?
    /// 0.5 to 2; nil is 1.
    var size: Double?
    var font: LetterStyle?
    var motion: MessageMotionKind = .none
    /// `{sweep:<seconds>}` as read; kept while Motion stays Sweep.
    var sweepPeriod: Double?

    static let maxInk = MessageMarkup.maxInkColors

    /// The tags in the editor's order: ink, glow, size, font, motion.
    var tags: [String] {
        var out: [String] = []
        if let ink, !ink.isEmpty { out.append("{ink:" + ink.map { "#" + $0 }.joined(separator: ",") + "}") }
        if let glow { out.append(glow ? "{glow}" : "{noglow}") }
        if let size, size != 1 { out.append("{size:\(Self.number(size))}") }
        if let font { out.append("{font:\(font.rawValue)}") }
        switch motion {
        case .none: break
        case .write: out.append("{write}")
        case .shimmer: out.append("{shimmer}")
        case .sweep: out.append(sweepPeriod.map { "{sweep:\(Self.number($0))}" } ?? "{sweep}")
        }
        return out
    }

    /// "1.25", "0.5", "2": at most two decimals, no trailing zeros.
    static func number(_ v: Double) -> String {
        var s = String(format: "%.2f", v)
        while s.hasSuffix("0") { s.removeLast() }
        if s.hasSuffix(".") { s.removeLast() }
        return s
    }

    // MARK: Color

    enum InkMode: String, CaseIterable, Hashable {
        case desk, solid, gradient

        var title: String {
            switch self {
            case .desk: "As the Desk"
            case .solid: "One color"
            case .gradient: "Gradient"
            }
        }
    }

    var inkMode: InkMode {
        guard let ink, !ink.isEmpty else { return .desk }
        return ink.count == 1 ? .solid : .gradient
    }

    mutating func setInkMode(_ mode: InkMode) {
        switch mode {
        case .desk: ink = nil
        case .solid: ink = [ink?.first ?? "ff2a6d"]
        case .gradient:
            let first = ink?.first ?? "ff2a6d"
            ink = (ink?.count ?? 0) > 1 ? ink : [first, "05d9e8"]
        }
    }

    mutating func addStop() {
        guard var stops = ink, stops.count >= 2, stops.count < Self.maxInk else { return }
        stops.append(stops.last ?? "ffffff")
        ink = stops
    }

    mutating func removeStop(at i: Int) {
        guard var stops = ink, stops.count > 2, stops.indices.contains(i) else { return }
        stops.remove(at: i)
        ink = stops
    }

    mutating func setStop(_ i: Int, hex: String) {
        guard var stops = ink, stops.indices.contains(i), MessageMarkup.isHex(hex) else { return }
        stops[i] = (hex.hasPrefix("#") ? String(hex.dropFirst()) : hex).lowercased()
        ink = stops
    }

    mutating func setMotion(_ kind: MessageMotionKind) {
        if kind != motion { sweepPeriod = nil }
        motion = kind
    }
}

/// The owner's now-playing mod's named palettes, a gradient each.
enum MessagePalette {
    static let all: [(name: String, colors: [String])] = [
        ("synthwave", ["ff2a6d", "d16ba5", "05d9e8"]),
        ("sunset", ["ff7e5f", "feb47b", "ffd86f"]),
        ("ocean", ["00c6ff", "4f8bff", "a18cff"]),
        ("aurora", ["00f5a0", "00d9f5", "a06bff"]),
        ("ember", ["f12711", "f5af19", "ffe259"]),
        ("bubblegum", ["ff9a9e", "f6a6d8", "a18cd1"]),
        ("toxic", ["b6ff00", "00ff9c", "00c3ff"]),
        ("gold", ["f7971e", "ffd200", "fff6b7"]),
    ]

    static func colors(_ name: String) -> [String]? {
        all.first { $0.name == name }?.colors
    }

    /// The palette these colors are, if they are one.
    static func name(of ink: [String]?) -> String? {
        guard let ink else { return nil }
        let lower = ink.map { $0.lowercased() }
        return all.first { $0.colors == lower }?.name
    }
}

/// The Letters menu's order (item 69): the statusline's letter styles (LetterStyle).
enum MessageLetters {
    static let order: [LetterStyle] = [.bold, .italic, .boldItalic, .smallCaps, .script, .fraktur,
                                       .doubleStruck, .sans, .mono]
}

/// A line the editor represents: when, text, look.
struct MessageLine: Hashable {
    var when: MessageWhen = .everyDay
    /// Plain text, no tags.
    var text = ""
    var look = MessageLook()

    /// The text as it can sit on one line.
    var cleanText: String {
        text.replacingOccurrences(of: "\r\n", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\r", with: " ")
            .trimmingCharacters(in: .whitespaces)
    }

    /// The text with its tags, no prefix: what the Desk parses, and what Pin holds.
    var body: String { (look.tags + [cleanText]).joined(separator: " ") }

    /// The line as messages.txt holds it; nil for a line with no text (left out when saved).
    var written: String? {
        cleanText.isEmpty ? nil : when.prefix + body
    }

    /// Why the Desk would read the written line otherwise, or nil when it reads back as this.
    var problem: String? {
        guard let written else { return nil }
        if case .line(let back) = MessageLineModel.content(of: written), back == normalized { return nil }
        let t = cleanText
        if t.hasPrefix("{") { return "Text can't start with {: the Desk reads that as an effect." }
        if t.hasPrefix("#") { return "A line starting with # is a note; the Desk skips it. Add a look or a day, or start with another character." }
        if when == .everyDay, MessageEngine.splitPrefix(t).kind != .plain {
            return "Text that starts like a day (\"Mon:\" or \"10-31:\") shows only on that day."
        }
        return "The Desk would read this line differently. Switch to Text to see it."
    }

    /// As it reads back: text cleaned, size 1 as none.
    var normalized: MessageLine {
        var n = self
        n.text = cleanText
        if n.look.size == 1 { n.look.size = nil }
        if n.look.motion != .sweep { n.look.sweepPeriod = nil }
        return n
    }
}

/// One row of messages.txt.
struct MessageRow: Identifiable, Hashable {
    enum Content: Hashable {
        /// A message line the editor represents.
        case line(MessageLine)
        /// A message line it can't; kept as written.
        case raw(String)
        case comment(String)
        case blank(String)
    }

    let id: UUID
    var content: Content
    /// The line as read (without a trailing \r), nil for a row added here.
    var source: String?
    /// What `source` read as: while `content` is still this, the row writes `source` exactly.
    var sourceContent: Content?
    /// The line ended in \r\n.
    var crlf = false

    init(id: UUID = UUID(), content: Content, source: String? = nil, crlf: Bool = false) {
        self.id = id
        self.content = content
        self.source = source
        self.sourceContent = source == nil ? nil : content
        self.crlf = crlf
    }

    /// A message line the Desk can show (styled or raw), not a comment or blank.
    var isMessage: Bool {
        switch content {
        case .line, .raw: true
        case .comment, .blank: false
        }
    }

    var line: MessageLine? {
        if case .line(let l) = content { return l }
        return nil
    }

    /// The row as written, nil when it is left out (a new line with no text).
    var written: String? {
        let text: String?
        if let source, content == sourceContent {
            text = source
        } else {
            switch content {
            case .line(let l): text = l.written
            case .raw(let s), .comment(let s), .blank(let s): text = s
            }
        }
        return text.map { crlf ? $0 + "\r" : $0 }
    }

    /// The body the Desk draws for this row (prefix gone, tags on), for its preview and Pin.
    var deskBody: String? {
        switch content {
        case .line(let l): return l.cleanText.isEmpty ? nil : l.body
        case .raw(let s):
            let t = s.trimmingCharacters(in: .whitespaces)
            return MessageEngine.splitPrefix(t).body
        case .comment, .blank: return nil
        }
    }
}

/// messages.txt as rows.
struct MessageDocument: Equatable {
    var rows: [MessageRow] = []
    /// The file ended with a newline (a new file does).
    var trailingNewline = true

    init(rows: [MessageRow] = [], trailingNewline: Bool = true) {
        self.rows = rows
        self.trailingNewline = trailingNewline
    }

    init(parsing text: String) {
        guard !text.isEmpty else { return }
        // By UTF-16 (Foundation), not by Character: "\r\n" is one Character in Swift.
        var pieces = text.components(separatedBy: "\n")
        trailingNewline = text.utf8.last == UInt8(ascii: "\n")
        if trailingNewline { pieces.removeLast() }
        rows = pieces.map { piece in
            let crlf = piece.hasSuffix("\r")
            let line = crlf ? String(piece.dropLast()) : piece
            return MessageRow(content: MessageLineModel.content(of: line), source: line, crlf: crlf)
        }
    }

    /// The file's text.
    var text: String {
        let lines = rows.compactMap(\.written)
        guard !lines.isEmpty else { return "" }
        return lines.joined(separator: "\n") + (trailingNewline ? "\n" : "")
    }

    /// The rows the list shows, in order.
    var messageRows: [MessageRow] { rows.filter(\.isMessage) }

    /// Comments and blank lines, kept where they are.
    var noteCount: Int { rows.count - messageRows.count }

    func index(of id: UUID) -> Int? { rows.firstIndex { $0.id == id } }

    // MARK: Edits; every other row is left as it is.

    /// A new line after the last row; its id.
    @discardableResult
    mutating func add(_ line: MessageLine = MessageLine()) -> UUID {
        let row = MessageRow(content: .line(line), crlf: rows.last?.crlf ?? false)
        rows.append(row)
        return row.id
    }

    /// Add Line (Settings v2, slice 3): a new line at the top of its day's lines, before the
    /// first message row with the same When; with none, before the first message row; with no
    /// message rows, after the last row. Notes and blank lines stay where they are. Its id.
    @discardableResult
    mutating func insertAtTopOfGroup(_ line: MessageLine = MessageLine()) -> UUID {
        let row = MessageRow(content: .line(line), crlf: rows.last?.crlf ?? false)
        let at = rows.firstIndex { $0.isMessage && Self.when(of: $0) == line.when }
            ?? rows.firstIndex(where: \.isMessage)
            ?? rows.count
        rows.insert(row, at: at)
        return row.id
    }

    /// A copy right after the row, written as the row is; its id.
    @discardableResult
    mutating func duplicate(_ id: UUID) -> UUID? {
        guard let i = index(of: id) else { return nil }
        var copy = rows[i]
        copy = MessageRow(id: UUID(), content: copy.content, source: copy.source, crlf: copy.crlf)
        copy.sourceContent = rows[i].sourceContent
        rows.insert(copy, at: i + 1)
        return copy.id
    }

    mutating func delete(_ id: UUID) {
        rows.removeAll { $0.id == id }
    }

    /// Changes a styled row's line.
    mutating func update(_ id: UUID, _ change: (inout MessageLine) -> Void) {
        guard let i = index(of: id), case .line(var line) = rows[i].content else { return }
        change(&line)
        rows[i].content = .line(line)
    }

    /// Drag and drop: the row goes after `target` when it came from above, before it otherwise.
    mutating func move(_ id: UUID, onto target: UUID) {
        guard id != target, let from = index(of: id), let to = index(of: target) else { return }
        let row = rows.remove(at: from)
        guard let t = index(of: target) else { rows.insert(row, at: from); return }
        rows.insert(row, at: from < to ? t + 1 : t)
    }

    /// Move Up / Move Down: past the neighbouring message row (comments stay where they are).
    mutating func move(_ id: UUID, by step: Int) {
        let messages = messageRows
        guard let k = messages.firstIndex(where: { $0.id == id }) else { return }
        let n = k + step
        guard messages.indices.contains(n) else { return }
        move(id, onto: messages[n].id)
    }

    /// After a save: each row's written line becomes its source; rows left out go.
    mutating func rebase() {
        rows = rows.compactMap { row in
            guard let written = row.written else { return nil }
            let line = row.crlf ? String(written.dropLast()) : written
            var r = MessageRow(id: row.id, content: row.content, source: line, crlf: row.crlf)
            r.sourceContent = row.content
            return r
        }
    }
}

enum MessageLineModel {
    /// What one line of messages.txt is, as the Desk reads it (MessageEngine.pick).
    static func content(of line: String) -> MessageRow.Content {
        let t = line.trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return .blank(line) }
        if t.hasPrefix("#") { return .comment(line) }
        let split = MessageEngine.splitPrefix(t)
        let when: MessageWhen
        switch split.kind {
        case .plain: when = .everyDay
        case .weekday: when = .weekday(split.tag)
        case .date:
            guard let date = MessageWhen.date(split.tag) else { return .raw(line) }
            when = date
        }
        guard case .success(let parsed) = MessageMarkup.parseStrict(split.body),
              let look = look(parsed.effects) else { return .raw(line) }
        return .line(MessageLine(when: when, text: parsed.text, look: look))
    }

    /// The effects as a look, nil when the controls can't hold them (two motions on one line).
    static func look(_ e: MessageEffects) -> MessageLook? {
        let motions = [e.write, e.shimmer, e.sweep].filter { $0 }.count
        guard motions <= 1 else { return nil }
        var look = MessageLook()
        look.ink = e.ink.map { $0.map { $0.lowercased() } }
        look.glow = e.glow
        look.size = e.size == 1 ? nil : e.size
        look.font = e.font
        if e.write { look.motion = .write }
        if e.shimmer { look.motion = .shimmer }
        if e.sweep {
            look.motion = .sweep
            look.sweepPeriod = e.sweepPeriod
        }
        return look
    }
}

// MARK: - What happens to a row (item 69)

extension MessageDocument {
    /// When a row shows, for its note and the counts; nil for one that never shows (no text yet, or
    /// a date that isn't one).
    static func when(of row: MessageRow) -> MessageWhen? {
        guard let body = row.deskBody, !body.isEmpty else { return nil }
        switch row.content {
        case .line(let l): return l.when
        case .raw(let s):
            let split = MessageEngine.splitPrefix(s.trimmingCharacters(in: .whitespaces))
            switch split.kind {
            case .plain: return .everyDay
            case .weekday: return .weekday(split.tag)
            case .date: return MessageWhen.date(split.tag)
            }
        case .comment, .blank: return nil
        }
    }

    /// The row's note: when it shows and what it does to the day's other lines, as
    /// MessageEngine.today picks them. `mix` is Settings, Message's mix switch.
    func note(for row: MessageRow, mix: Bool) -> String {
        guard let when = Self.when(of: row) else {
            return row.line == nil ? "As written · never shows" : "Shows once it has text"
        }
        let all = rows.compactMap(Self.when(of:))
        let others = all.filter { $0 == when }.count - 1
        let hasWeekdays = all.contains { if case .weekday = $0 { true } else { false } }
        var parts = [when.label]
        switch when {
        case .date:
            parts.append("shows above the day's message")
            if others > 0 { parts.append("with \(Self.others(others)) that day") }
            if others + 1 > MessageEngine.maxSpecial {
                parts.append("\(MessageEngine.maxSpecial) at a time, taking turns hourly")
            }
        case .weekday:
            if others > 0 { parts.append("takes turns with \(Self.others(others))") }
            if mix {
                parts.append("mixes with every-day lines")
            } else if others == 0 {
                parts.append("shows instead of the every-day lines")
            }
        case .everyDay:
            if others > 0 { parts.append("takes turns with \(Self.others(others))") }
            if hasWeekdays {
                parts.append(mix ? "mixes in on days with their own line" : "steps aside on days with their own line")
            }
        }
        return parts.joined(separator: " · ")
    }

    /// "1 other", "3 others".
    static func others(_ n: Int) -> String { n == 1 ? "1 other" : "\(n) others" }
}
