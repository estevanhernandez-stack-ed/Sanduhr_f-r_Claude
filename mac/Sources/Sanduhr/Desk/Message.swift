import Foundation

/// The desktop message: the line in your handwriting that used to be baked into the wallpaper.
/// Desk draws it live now, so the layout keeps it clear of the clock and meetings, and it can
/// change by day without regenerating an image.
///
/// Lines come from ~/Library/Application Support/Desk/messages.txt (created on first run):
///   keep building.                 any day
///   Mon: one thing at a time.      only on Mondays (Mon Tue Wed Thu Fri Sat Sun)
///   10-31: happy halloween.        on that date (MM-DD), above the day's usual line
///   # a comment                    ignored, as are blank lines
///   {ink:#ff2a6d,#05d9e8} {glow} hi   effects at the start of the text (MessageEffects.swift)
///
/// Each day shows its usual line and, on a date with its own lines, those too (item 69):
/// - The usual line: today's weekday lines if there are any, else the plain lines. With
///   `messageMixDaily` on, the weekday lines and the plain lines take turns together instead.
///   Plain lines rotate by day; a weekday's lines rotate by week (that weekday comes round once a
///   week, so stepping by day would stall on a pool whose size shares a factor with 7).
/// - Special lines: every line for today's date shows, stacked above the usual line, up to
///   `maxSpecial`; more than that take turns hourly, `maxSpecial` at a time.
/// `messageRotate hourly` turns every pool hourly, steady within the hour.
/// defaults write com.626labs.sanduhr.desk message "text" pins one line: it replaces the usual line,
/// and the date's lines still stack above it.
enum MessageEngine {
    static var fileURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Desk/messages.txt")
    }

    /// Settings, Message: "Mix every-day lines in on days with their own line", off by default.
    static let mixKey = "messageMixDaily"
    /// Lines for one date shown at once.
    static let maxSpecial = 3

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

    /// What the Desk draws today: the date's special lines (tags on, prefix gone) and the usual
    /// line. A pinned line is the usual line; the date's lines still show above it.
    struct Today: Equatable {
        var special: [String] = []
        var usual: String?

        /// Special lines first, then the usual one.
        var lines: [String] { special + [usual].compactMap { $0 } }
        /// For places with room for one line: the first special line, else the usual one.
        var first: String? { special.first ?? usual }
    }

    static func today(now: Date = Date()) -> Today {
        let d = UserDefaults.desk
        let pinned = d.string(forKey: "message").flatMap { $0.isEmpty ? nil : $0 }
        guard let text = try? String(contentsOf: fileURL, encoding: .utf8) else { return Today(usual: pinned) }
        return today(from: text, now: now, hourly: d.string(forKey: "messageRotate") == "hourly",
                     mix: d.bool(forKey: mixKey), pinned: pinned)
    }

    /// The usual line (or the pin); `today(now:)` has the special lines too.
    static func current(now: Date = Date()) -> String? {
        today(now: now).usual
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

    /// The file's message lines, sorted into pools by prefix.
    struct Pools: Equatable {
        /// Date lines by "MM-DD", in file order.
        var dated: [String: [String]] = [:]
        /// Weekday lines by "Mon" to "Sun", in file order.
        var weekday: [String: [String]] = [:]
        var plain: [String] = []
    }

    static func pools(_ text: String) -> Pools {
        var p = Pools()
        for raw in text.components(separatedBy: .newlines) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            let split = splitPrefix(line)
            switch split.kind {
            case .date: if !split.body.isEmpty { p.dated[split.tag, default: []].append(split.body) }
            case .weekday: if !split.body.isEmpty { p.weekday[split.tag, default: []].append(split.body) }
            case .plain: p.plain.append(line)
            }
        }
        return p
    }

    /// `pinned` replaces the usual line only: the date's lines still stack above it.
    static func today(from text: String, now: Date, hourly: Bool, mix: Bool = false,
                      pinned: String? = nil, calendar: Calendar = .current) -> Today {
        let p = pools(text)
        let comps = calendar.dateComponents([.month, .day, .weekday, .hour], from: now)
        let date = String(format: "%02d-%02d", comps.month ?? 0, comps.day ?? 0)
        let weekday = weekdays[((comps.weekday ?? 1) - 1) % 7]
        let hour = comps.hour ?? 0
        let dayIndex = calendar.ordinality(of: .day, in: .era, for: now) ?? 0
        let weekIndex = dayIndex / 7
        func turn(_ pool: [String], _ step: Int) -> String? {
            guard !pool.isEmpty else { return nil }
            let slot = hourly ? step * 24 + hour : step
            return pool[slot % pool.count]
        }
        let days = p.weekday[weekday] ?? []
        let usual: String?
        if let pinned {
            usual = pinned
        } else if days.isEmpty {
            usual = turn(p.plain, dayIndex)
        } else {
            usual = turn(mix ? days + p.plain : days, weekIndex)
        }
        let year = calendar.component(.year, from: now)
        return Today(special: special(p.dated[date] ?? [], year: year, hour: hour), usual: usual)
    }

    /// A date's lines: all of them up to `maxSpecial`; past that, `maxSpecial` at a time, the
    /// window moving on each hour (and each year), wrapping round the list.
    static func special(_ lines: [String], year: Int, hour: Int) -> [String] {
        guard lines.count > maxSpecial else { return lines }
        let start = ((year * 24 + hour) * maxSpecial) % lines.count
        return (0..<maxSpecial).map { lines[(start + $0) % lines.count] }
    }

    /// The usual line (the Desk's single-line pick before item 69).
    static func pick(from text: String, now: Date, hourly: Bool, mix: Bool = false,
                     calendar: Calendar = .current) -> String? {
        today(from: text, now: now, hourly: hourly, mix: mix, calendar: calendar).usual
    }
}

/// On special days (item 69): how the date's lines and the usual line share the message piece.
/// Stack draws them all, the date's lines on top; Take turns shows one at a time with a crossfade;
/// Scroll moves them up one after another like a slow ticker. The line showing is decided by the
/// clock alone (`index(at:)`), so the Desk, the notch and the previews agree without talking.
enum MessageSpecialMode: String, CaseIterable, Sendable {
    case stack, turns, scroll

    static let key = "messageSpecialMode"
    static let secondsKey = "messageSpecialSeconds"
    static let defaultSeconds: Double = 10
    /// The timing menu's presets.
    static let presets: [Double] = [5, 10, 30, 60, 300]

    var title: String {
        switch self {
        case .stack: "Stack"
        case .turns: "Take turns"
        case .scroll: "Scroll"
        }
    }

    var help: String {
        switch self {
        case .stack: "The day's special lines sit above its usual line, all at once."
        case .turns: "One line at a time, fading from the special lines to the usual line and round again."
        case .scroll: "One line at a time, each gliding up out of view as the next comes in, like a slow ticker."
        }
    }

    /// "5 s", "10 s", "30 s", "1 min", "5 min".
    static func label(_ seconds: Double) -> String {
        seconds >= 60 ? "\(Int(seconds / 60)) min" : "\(Int(seconds)) s"
    }

    /// The saved choice; anything unknown is Stack.
    static func saved(in d: UserDefaults = .desk) -> MessageSpecialMode {
        d.string(forKey: key).flatMap(MessageSpecialMode.init(rawValue:)) ?? .stack
    }

    /// The saved time each line shows, one of the presets (10 s unless set).
    static func savedSeconds(in d: UserDefaults = .desk) -> Double {
        let v = d.double(forKey: secondsKey)
        return presets.contains(v) ? v : defaultSeconds
    }

    /// How a change of line is drawn.
    enum Change: Equatable { case none, crossfade, scroll }

    /// Reduce Motion: Take turns swaps at once, and Scroll does the same.
    static func change(_ mode: MessageSpecialMode, reduceMotion: Bool) -> Change {
        switch mode {
        case .stack: .none
        case .turns: reduceMotion ? .none : .crossfade
        case .scroll: reduceMotion ? .none : .scroll
        }
    }

    /// The crossfade's length.
    static let fade: TimeInterval = 0.4
    /// Scroll's glide: the old line up and out while the new one comes up in.
    static let glide: TimeInterval = 0.8

    /// Whether the lines cycle: Take turns or Scroll on a special day (more than one line).
    static func cycles(_ mode: MessageSpecialMode, today: MessageEngine.Today) -> Bool {
        mode != .stack && !today.special.isEmpty && today.lines.count > 1
    }

    /// Which of `count` lines shows at `date`, each for `seconds`, by the clock.
    static func index(at date: Date, count: Int, seconds: Double) -> Int {
        guard count > 1, seconds > 0 else { return 0 }
        let step = Int(floor(date.timeIntervalSince1970 / seconds))
        return ((step % count) + count) % count
    }

    /// When the line next changes after `date`.
    static func nextChange(after date: Date, seconds: Double) -> Date {
        let step = floor(date.timeIntervalSince1970 / seconds) + 1
        return Date(timeIntervalSince1970: step * seconds)
    }
}
