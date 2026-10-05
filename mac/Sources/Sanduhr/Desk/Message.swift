import Foundation

/// The desktop message: the line in your handwriting that used to be baked into the wallpaper.
/// Desk draws it live now, so the layout keeps it clear of the clock and meetings, and it can
/// change by day without regenerating an image.
///
/// Lines come from ~/Library/Application Support/Desk/messages.txt (created on first run):
///   keep building.                 any day
///   Mon: one thing at a time.      only on Mondays (Mon Tue Wed Thu Fri Sat Sun)
///   10-31: happy halloween.        only on that date (MM-DD); beats weekday and plain lines
///   # a comment                    ignored, as are blank lines
///   {ink:#ff2a6d,#05d9e8} {glow} hi   effects at the start of the text (MessageEffects.swift)
/// The most specific pool that has lines wins; within it the pick rotates once a day
/// (defaults write com.626labs.sanduhr.desk messageRotate hourly for every hour), steady in between.
/// defaults write com.626labs.sanduhr.desk message "text" pins one line and skips the file.
enum MessageEngine {
    static var fileURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Desk/messages.txt")
    }

    static let starter = """
    # Desk messages. One per line; Desk picks one a day.
    #   Mon: text      only on Mondays
    #   10-31: text    only on that date
    #   {ink:#ff2a6d,#05d9e8} {glow} {size:1.2} {write} {shimmer} text    effects
    keep building.
    Mon: one thing at a time.
    Fri: showtime.
    """

    /// Creates the starter file the first time, so there is something to edit.
    static func ensureFile() {
        let url = fileURL
        guard !FileManager.default.fileExists(atPath: url.path) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                 withIntermediateDirectories: true)
        try? (starter + "\n").write(to: url, atomically: true, encoding: .utf8)
    }

    static func current(now: Date = Date()) -> String? {
        let d = UserDefaults.desk
        if let pinned = d.string(forKey: "message"), !pinned.isEmpty { return pinned }
        guard let text = try? String(contentsOf: fileURL, encoding: .utf8) else { return nil }
        return pick(from: text, now: now, hourly: d.string(forKey: "messageRotate") == "hourly")
    }

    static let weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]

    enum PrefixKind { case date, weekday, plain }

    /// A trimmed line's prefix: "10-31: text" is a date line, "Mon: text" a weekday line, anything
    /// else (a colon after other words included) is plain and its body is the whole line. The
    /// body keeps its effect tags (MessageMarkup); DeskView reads them.
    static func splitPrefix(_ line: String) -> (kind: PrefixKind, tag: String, body: String) {
        if let colon = line.firstIndex(of: ":") {
            let tag = String(line[..<colon]).trimmingCharacters(in: .whitespaces)
            let body = String(line[line.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
            if tag.range(of: #"^\d{2}-\d{2}$"#, options: .regularExpression) != nil { return (.date, tag, body) }
            if weekdays.contains(tag) { return (.weekday, tag, body) }
        }
        return (.plain, "", line)
    }

    static func pick(from text: String, now: Date, hourly: Bool, calendar: Calendar = .current) -> String? {
        let comps = calendar.dateComponents([.month, .day, .weekday, .hour], from: now)
        let today = String(format: "%02d-%02d", comps.month ?? 0, comps.day ?? 0)
        let weekday = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"][((comps.weekday ?? 1) - 1) % 7]

        var dated: [String] = [], daily: [String] = [], plain: [String] = []
        for raw in text.components(separatedBy: .newlines) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            let split = splitPrefix(line)
            switch split.kind {
            case .date: if split.tag == today, !split.body.isEmpty { dated.append(split.body) }
            case .weekday: if split.tag == weekday, !split.body.isEmpty { daily.append(split.body) }
            case .plain: plain.append(line)
            }
        }
        let pool = !dated.isEmpty ? dated : (!daily.isEmpty ? daily : plain)
        guard !pool.isEmpty else { return nil }
        let dayIndex = calendar.ordinality(of: .day, in: .era, for: now) ?? 0
        let slot = hourly ? dayIndex * 24 + (comps.hour ?? 0) : dayIndex
        return pool[slot % pool.count]
    }
}
