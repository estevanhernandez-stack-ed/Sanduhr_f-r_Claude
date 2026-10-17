import Foundation

/// The meters mod's band in Swift (item 75, slice B): a port of the mod's `meters.ts`, `band.ts`
/// and `draw.ts`, so the Mod Studio draws its preview by the rules a session draws the band.
/// `Fixtures/band-frames/frames.json` is written from the mod's own code
/// (`mac/integrations/band-fixtures/generate.ts`) and BandFrameTests replays every case: a change
/// on either side that the other doesn't match fails. Times are milliseconds since 1970, as in
/// the mod. Pure: no files, no clock.
enum BandFrame {
    // MARK: Constants (meters.ts, band.ts, draw.ts)

    static let schemaVersion = 1
    static let freshSeconds = 450.0
    static let deadSeconds = 900.0
    static let session = "five_hour"
    static let weekly = "seven_day"
    static let hour = 3_600_000.0
    static let day = 24 * hour
    static let warnPercent = 90
    static let paceColor = "#f472b6"
    static let warnRed = "#f87171"
    static let white = "#ffffff"
    static let plain = "#9ca3af"
    static let sweepMS = 1100.0
    static let periodMS = 4000.0
    static let shimmerBeforeMS = 10 * 60_000.0
    static let fadeMS = 6000.0
    static let staleMS = 3 * 60_000.0
    static let maxWatchers = 24
    static let maxRows = 6
    static let barWidth: [Style: Int] = [.compact: 10, .full: 20]
    static let narrowBar = 6
    static let wideRow = 60
    static let fullColumns = 64
    static let narrowColumns = 80

    // MARK: Types

    enum Bars: String, CaseIterable, Sendable { case both, session, weekly }
    enum Style: String, CaseIterable, Sendable { case compact, full }

    struct Tier: Equatable, Sendable {
        var key: String
        var utilization: Int?
        var resetsAt: Double?
    }

    enum Snapshot: Equatable, Sendable {
        case newer
        case reading(capturedAt: Double, isError: Bool, errorKind: String?, tiers: [Tier])
    }

    /// A segment's look as band.json carries it (the statusline's style grammar).
    struct Look: Equatable, Sendable {
        var ink: [String] = []
        var font: String?
        var bold = false
        var italic = false
        var dim = false
        var underline = false
    }

    enum WatcherState: String, CaseIterable, Sendable {
        case running, waiting, passed, failed, finished
        case lostTouch = "lost_touch"
    }

    enum Watcher: Equatable, Sendable {
        case agent(title: String, short: String?, state: WatcherState, done: Int?, total: Int?, startedAt: Double?, endedAt: Double?)
        case automatic(kind: String, state: WatcherState)

        var state: WatcherState {
            switch self {
            case .agent(_, _, let s, _, _, _, _), .automatic(_, let s): s
            }
        }
    }

    /// band.json, read.
    struct BandFile: Equatable, Sendable {
        var writtenAt: Double
        var reduceMotion = false
        /// session, weekly, resets.
        var styles: [String: Look] = [:]
        var watchers: [Watcher]?
    }

    struct Meter: Equatable, Sendable {
        var key: String
        var name: String
        var isCrossed: Bool
        var pct: Int
        var color: String
        var fraction: Double?
        var pace: String?
        var isWarning: Bool
        var reset: String?
        var resetLeft: Double?
    }

    enum Band: Equatable, Sendable {
        case none
        case line(String)
        case meters([Meter], isDim: Bool, note: String?)
    }

    /// One run of characters as drawn: its color (nil: the terminal's own) and attributes.
    struct Cell: Equatable, Sendable, Codable {
        var text: String
        var color: String?
        var bold = false
        var italic = false
        var underline = false
        var dim = false
    }

    struct Input: Equatable, Sendable {
        var now: Double
        var bars: Bars = .both
        var style: Style = .compact
        var label = ""
        /// The motion option and band.json's Reduce Motion together.
        var moving = true
        var width = 120
        var maxRows = 4
        var tzOffsetMinutes = 0
        var seat = false
        /// When each limit's one-shot sweep started.
        var sweeps: [String: Double] = [:]
    }

    // MARK: JSON helpers

    private static func isBool(_ v: Any?) -> Bool {
        guard let n = v as? NSNumber else { return false }
        return CFGetTypeID(n) == CFBooleanGetTypeID()
    }

    private static func number(_ v: Any?) -> Double? {
        guard let n = v as? NSNumber, !isBool(n) else { return nil }
        let d = n.doubleValue
        return d.isFinite ? d : nil
    }

    private static func bool(_ v: Any?) -> Bool? {
        isBool(v) ? (v as? NSNumber)?.boolValue : nil
    }

    private static func object(_ data: Data) -> [String: Any]? {
        (try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])) as? [String: Any]
    }

    /// An ISO-8601 time in ms, or nil. Fractions are cut to milliseconds (.NET writes seven digits).
    static func parseTime(_ v: Any?) -> Double? {
        guard let s = v as? String, let d = ISO8601.parse(s) else { return nil }
        return (d.timeIntervalSince1970 * 1000).rounded()
    }

    // MARK: snapshot.json (meters.ts)

    static func parseSnapshot(_ data: Data) -> Snapshot? {
        guard let o = object(data) else { return nil }
        let version: Double?
        if let n = number(o["schema_version"]) { version = n } else if let s = o["schema_version"] as? String { version = Double(s) } else { version = nil }
        guard let version else { return nil }
        if version > Double(schemaVersion) { return .newer }
        guard let capturedAt = parseTime(o["captured_at"]) else { return nil }
        var tiers: [Tier] = []
        for t in (o["tiers"] as? [Any]) ?? [] {
            guard let r = t as? [String: Any] else { continue }
            let util = number(r["utilization"]).map { Int($0.rounded(.towardZero)) }
            let key = r["key"].map { ($0 as? String) ?? "\($0)" } ?? ""
            tiers.append(Tier(key: key, utilization: util, resetsAt: parseTime(r["resets_at"])))
        }
        return .reading(capturedAt: capturedAt, isError: (o["status"] as? String) == "error",
                        errorKind: o["error_kind"] as? String, tiers: tiers)
    }

    // MARK: band.json (band.ts)

    static let fonts = ["bold", "italic", "bold-italic", "script", "fraktur", "double-struck", "sans", "mono", "small-caps"]
    private static let styleKeys: Set<String> = ["ink", "font", "bold", "italic", "dim", "underline"]

    /// "#abc" and "abc" as "#aabbcc"; nil for anything else.
    static func normalizeHex(_ v: Any?) -> String? {
        guard let s = (v as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) else { return nil }
        let h = s.hasPrefix("#") ? String(s.dropFirst()) : s
        guard h.count == 3 || h.count == 6, h.allSatisfy(\.isHexDigit), h.allSatisfy(\.isASCII) else { return nil }
        let l = h.lowercased()
        return "#" + (l.count == 3 ? l.map { "\($0)\($0)" }.joined() : l)
    }

    /// A style as the statusline's picks carry it, or nil when it is anything else.
    static func parseStyle(_ v: Any?) -> Look? {
        guard let o = v as? [String: Any], o.keys.allSatisfy(styleKeys.contains) else { return nil }
        var look = Look()
        if let ink = o["ink"] {
            guard let a = ink as? [Any], (1...4).contains(a.count) else { return nil }
            let colors = a.map(normalizeHex)
            guard colors.allSatisfy({ $0 != nil }) else { return nil }
            look.ink = colors.compactMap { $0 }
        }
        if let f = o["font"] {
            guard let s = f as? String, fonts.contains(s) else { return nil }
            look.font = s
        }
        for k in ["bold", "italic", "dim", "underline"] where o[k] != nil {
            guard bool(o[k]) != nil else { return nil }
        }
        look.bold = bool(o["bold"]) == true
        look.italic = bool(o["italic"]) == true
        look.dim = bool(o["dim"]) == true
        look.underline = bool(o["underline"]) == true
        return look
    }

    /// One line of text, no control characters, at most `cap` characters; nil when empty.
    private static func line(_ v: Any?, _ cap: Int) -> String? {
        guard let s = v as? String else { return nil }
        let flat = String(String.UnicodeScalarView(s.unicodeScalars.map { u in
            (u.value <= 0x1f || (0x7f...0x9f).contains(u.value)) ? " " : u
        })).trimmingCharacters(in: .whitespacesAndNewlines)
        let scalars = Array(flat.unicodeScalars)
        guard !scalars.isEmpty else { return nil }
        if scalars.count > cap {
            return String(String.UnicodeScalarView(scalars.prefix(cap - 1))).trimmingCharacters(in: .whitespacesAndNewlines) + "…"
        }
        return flat
    }

    private static func count(_ v: Any?) -> Int? {
        guard let d = number(v), d == d.rounded(), d >= 0, d <= 1_000_000 else { return nil }
        return Int(d)
    }

    static func parseWatcher(_ v: Any?) -> Watcher? {
        guard let o = v as? [String: Any], let state = (o["state"] as? String).flatMap(WatcherState.init(rawValue:)) else { return nil }
        if (o["source"] as? String) == "automatic" {
            return line(o["kind"], 20).map { .automatic(kind: $0, state: state) }
        }
        guard (o["source"] as? String) == "agent", let title = line(o["title"], 80) else { return nil }
        let total = count(o["total"])
        let done = total.map { min(count(o["done"]) ?? 0, $0) }
        return .agent(title: title, short: line(o["short"], 12), state: state, done: done, total: total,
                      startedAt: parseTime(o["started_at"]), endedAt: parseTime(o["ended_at"]))
    }

    static func parseBand(_ data: Data) -> BandFile? {
        guard let o = object(data), number(o["schema_version"]) == Double(1),
              let writtenAt = parseTime(o["written_at"]) else { return nil }
        var file = BandFile(writtenAt: writtenAt, reduceMotion: bool(o["reduce_motion"]) == true)
        let all = ((o["meters"] as? [String: Any])?["styles"] as? [String: Any]) ?? [:]
        for key in ["session", "weekly", "resets"] {
            if let s = parseStyle(all[key]) { file.styles[key] = s }
        }
        if let ws = o["watchers"] as? [Any] {
            file.watchers = ws.prefix(maxWatchers).compactMap(parseWatcher)
        }
        return file
    }

    // MARK: The meters (meters.ts)

    static func freshness(_ capturedAt: Double, _ now: Double) -> String {
        let age = max(0, (now - capturedAt) / 1000)
        if age < freshSeconds { return "fresh" }
        return age <= deadSeconds ? "stale" : "dead"
    }

    static func paceFraction(_ key: String, _ resetsAt: Double?, _ now: Double) -> Double? {
        let total: Double? = key == session ? 5 * hour : key == weekly ? 7 * day : nil
        guard let resetsAt, let total else { return nil }
        let rem = max(0, resetsAt - now)
        return min(1, max(0, (total - rem) / total))
    }

    static func paceWords(_ pct: Int, _ fraction: Double?) -> String? {
        guard let fraction else { return nil }
        let diff = Double(pct) - fraction * 100
        if abs(diff) < 5 { return "on pace" }
        return "\(Int(abs(diff).rounded(.towardZero)))% \(diff > 0 ? "ahead" : "under")"
    }

    static func isWarning(_ key: String, _ pct: Int, _ resetsAt: Double?, _ now: Double) -> Bool {
        let minReset: Double? = key == session ? hour : key == weekly ? day : nil
        guard let minReset, pct >= warnPercent else { return false }
        return resetsAt == nil || resetsAt! - now > minReset
    }

    static func usageColor(_ pct: Int) -> String {
        pct < 50 ? "#4ade80" : pct < 75 ? "#facc15" : pct < 90 ? "#fb923c" : "#f87171"
    }

    enum BarPart: Equatable { case fill, pace, empty }

    /// A bar `width` cells wide, one cell each: full blocks, one partial eighth, the pace mark, the rest empty.
    static func bar(_ pct: Int, _ fraction: Double?, _ width: Int) -> [(ch: String, part: BarPart)] {
        let eighths = ["", "▏", "▎", "▍", "▌", "▋", "▊", "▉"]
        let filled = (Double(min(100, max(0, pct))) / 100) * Double(width)
        let full = Int(filled.rounded(.down))
        let eighth = Int(((filled - Double(full)) * 8 + 0.5).rounded(.down))
        var cells: [(ch: String, part: BarPart)] = (0..<width).map { i in
            if i < full { return ("█", .fill) }
            if i == full && eighth == 8 { return ("█", .fill) }
            if i == full && eighth > 0 { return (eighths[eighth], .fill) }
            return ("░", .empty)
        }
        if let fraction, width > 0 {
            let at = min(width - 1, max(0, Int((fraction * Double(width)).rounded(.down))))
            cells[at] = ("│", .pace)
        }
        return cells
    }

    static func countdown(_ ms: Double) -> String {
        let minutes = Int((max(0, ms) / 60_000).rounded(.down))
        if minutes <= 0 { return "now" }
        let d = minutes / 1440, h = (minutes % 1440) / 60, m = minutes % 60
        if d > 0 { return h > 0 ? "\(d)d \(h)h" : "\(d)d" }
        return h > 0 ? "\(h)h \(m)m" : "\(m)m"
    }

    private static let days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]

    private static func utcParts(_ ms: Double) -> (weekday: Int, hour: Int, minute: Int) {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let c = cal.dateComponents([.weekday, .hour, .minute], from: Date(timeIntervalSince1970: (ms / 1000).rounded(.down)))
        return ((c.weekday ?? 1) - 1, c.hour ?? 0, c.minute ?? 0)
    }

    static func clockTime(_ at: Double, _ tz: Int, withDay: Bool) -> String {
        let p = utcParts(at - Double(tz) * 60_000)
        let h12 = p.hour % 12 == 0 ? 12 : p.hour % 12
        let time = "\(h12):\(String(format: "%02d", p.minute)) \(p.hour < 12 ? "AM" : "PM")"
        return withDay ? "\(days[p.weekday]) \(time)" : time
    }

    static func resetWords(_ resetsAt: Double?, _ now: Double, _ style: Style, _ tz: Int) -> String? {
        guard let resetsAt else { return nil }
        let left = resetsAt - now
        if left > day {
            return style == .full ? "resets \(clockTime(resetsAt, tz, withDay: true))"
                : "resets \(days[utcParts(resetsAt - Double(tz) * 60_000).weekday])"
        }
        return style == .full ? "resets in \(countdown(left))" : "resets \(countdown(left))"
    }

    static func seatAge(_ ms: Double) -> String {
        let m = Int((max(0, ms) / 60_000).rounded(.down))
        return m < 60 ? "\(m)m" : m < 48 * 60 ? "\(m / 60)h" : "\(m / 1440)d"
    }

    static func bandFor(_ snap: Snapshot?, now: Double, bars: Bars, style: Style, tz: Int, seat: Bool) -> Band {
        guard let snap else { return .none }
        guard case let .reading(capturedAt, isError, errorKind, tiers) = snap else {
            return .line("Sanduhr: this snapshot is newer than the meters mod. Update the mod from Sanduhr.")
        }
        let age = freshness(capturedAt, now)
        if age == "dead" && !seat {
            return .line("Sanduhr: no update for \(Int(((now - capturedAt) / 60_000).rounded(.down)))m. Is the widget running?")
        }
        var meters: [Meter] = []
        for t in tiers {
            let wanted = t.key == session ? bars != .weekly : t.key == weekly ? bars != .session : false
            guard wanted, let util = t.utilization else { continue }
            let crossed = t.resetsAt.map { $0 <= now } ?? false
            let fraction = crossed ? nil : paceFraction(t.key, t.resetsAt, now)
            meters.append(Meter(
                key: t.key, name: t.key == session ? "Session" : t.key == weekly ? "Weekly" : t.key,
                isCrossed: crossed, pct: util, color: usageColor(util), fraction: fraction,
                pace: crossed ? nil : paceWords(util, fraction),
                isWarning: !crossed && !isError && isWarning(t.key, util, t.resetsAt, now),
                reset: crossed ? nil : resetWords(t.resetsAt, now, style, tz),
                resetLeft: crossed || t.resetsAt == nil ? nil : t.resetsAt! - now))
        }
        if isError {
            let words = ["session_expired": "sign in again", "cloudflare": "blocked, sign in again"][errorKind ?? ""] ?? "offline"
            if tiers.isEmpty {
                return .line(errorKind == "session_expired" ? "Sanduhr: sign in to see your meters." : "Sanduhr: \(words).")
            }
            return meters.isEmpty ? .line("Sanduhr: \(words).") : .meters(meters, isDim: true, note: "last known, \(words)")
        }
        if meters.isEmpty { return .none }
        if age == "dead" { return .meters(meters, isDim: true, note: "as of \(seatAge(now - capturedAt))") }
        if age == "stale" { return .meters(meters, isDim: true, note: "(\(Int(((now - capturedAt) / 60_000).rounded(.down)))m ago)") }
        return .meters(meters, isDim: false, note: nil)
    }

    // MARK: Letters and colors (band.ts)

    private static let blocks: [String: (upper: UInt32, lower: UInt32, digit: UInt32?, holes: [Character: UInt32])] = [
        "bold": (0x1d400, 0x1d41a, 0x1d7ce, [:]),
        "italic": (0x1d434, 0x1d44e, nil, ["h": 0x210e]),
        "bold-italic": (0x1d468, 0x1d482, nil, [:]),
        "script": (0x1d49c, 0x1d4b6, nil, ["B": 0x212c, "E": 0x2130, "F": 0x2131, "H": 0x210b, "I": 0x2110, "L": 0x2112, "M": 0x2133, "R": 0x211b, "e": 0x212f, "g": 0x210a, "o": 0x2134]),
        "fraktur": (0x1d504, 0x1d51e, nil, ["C": 0x212d, "H": 0x210c, "I": 0x2111, "R": 0x211c, "Z": 0x2128]),
        "double-struck": (0x1d538, 0x1d552, 0x1d7d8, ["C": 0x2102, "H": 0x210d, "N": 0x2115, "P": 0x2119, "Q": 0x211a, "R": 0x211d, "Z": 0x2124]),
        "sans": (0x1d5a0, 0x1d5ba, 0x1d7e2, [:]),
        "mono": (0x1d670, 0x1d68a, 0x1d7f6, [:]),
    ]
    private static let smallCaps = Array("ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘꞯʀꜱᴛᴜᴠᴡxʏᴢ".unicodeScalars)

    private static func scalar(_ v: UInt32) -> String { String(Character(Unicode.Scalar(v)!)) }

    static func letter(_ u: Unicode.Scalar, _ font: String?) -> String {
        let v = u.value
        let isUpper = (65...90).contains(v), isLower = (97...122).contains(v), isDigit = (48...57).contains(v)
        guard let font, isUpper || isLower || isDigit else { return String(Character(u)) }
        if font == "small-caps" { return isLower ? String(Character(smallCaps[Int(v - 97)])) : String(Character(u)) }
        guard let b = blocks[font] else { return String(Character(u)) }
        if let hole = b.holes[Character(u)] { return scalar(hole) }
        if isUpper { return scalar(b.upper + v - 65) }
        if isLower { return scalar(b.lower + v - 97) }
        return b.digit.map { scalar($0 + v - 48) } ?? String(Character(u))
    }

    /// `text` as characters (code points) in `font`.
    static func letters(_ text: String, _ font: String?) -> [String] {
        text.unicodeScalars.map { letter($0, font) }
    }

    private static func rgb(_ hex: String) -> [Double] {
        let h = String((normalizeHex(hex) ?? white).dropFirst())
        return [0, 2, 4].map { i in
            let s = h.index(h.startIndex, offsetBy: i)
            return Double(Int(h[s..<h.index(s, offsetBy: 2)], radix: 16) ?? 255)
        }
    }

    private static func toHex(_ c: [Double]) -> String {
        "#" + c.map { String(format: "%02x", Int(max(0, min(255, ($0 + 0.5).rounded(.down))))) }.joined()
    }

    /// `a` moved toward `b` by `t` (0..1).
    static func mix(_ a: String, _ b: String, _ t: Double) -> String {
        let x = rgb(a), y = rgb(b), k = max(0, min(1, t))
        return toHex(zip(x, y).map { $0 + ($1 - $0) * k })
    }

    static func gradient(_ ink: [String], _ n: Int) -> [String] {
        guard !ink.isEmpty, n > 0 else { return [] }
        if ink.count == 1 || n == 1 { return Array(repeating: normalizeHex(ink[0]) ?? white, count: n) }
        return (0..<n).map { i in
            let t = (Double(i) / Double(n - 1)) * Double(ink.count - 1)
            let k = min(Int(t.rounded(.down)), ink.count - 2)
            return mix(ink[k], ink[k + 1], t - Double(k))
        }
    }

    static func lightAt(_ i: Int, _ n: Int, _ t: Double) -> Double {
        let center = -1 + t * Double(n + 1)
        return max(0, 1 - abs(Double(i) - center) / 2)
    }

    static func onceProgress(_ start: Double?, _ now: Double) -> Double? {
        guard let start, now >= start, now - start < sweepMS else { return nil }
        return (now - start) / sweepMS
    }

    static func pulseProgress(_ now: Double) -> Double? {
        let phase = (now.truncatingRemainder(dividingBy: periodMS) + periodMS).truncatingRemainder(dividingBy: periodMS)
        return phase < sweepMS ? phase / sweepMS : nil
    }

    static func glowLevel(_ t: Double?) -> Double {
        t.map { sin(Double.pi * $0) * 0.6 } ?? 0
    }

    struct Run: Equatable { var text: String; var color: String? }

    static func paint(_ chars: [String], _ base: [String?], _ t: Double?, glow: Double = 0, offset: Int = 0, span: Int? = nil) -> [Run] {
        let span = span ?? chars.count
        var runs: [Run] = []
        for (i, ch) in chars.enumerated() {
            let own = i < base.count ? base[i] : nil
            let light = max(t.map { lightAt(i + offset, span, $0) * 0.85 } ?? 0, glow)
            let color = light > 0.01 ? mix(own ?? plain, white, light) : own
            if let last = runs.last, last.color == color {
                runs[runs.count - 1].text += ch
            } else {
                runs.append(Run(text: ch, color: color))
            }
        }
        return runs
    }

    // MARK: Watchers (band.ts)

    static let marks: [WatcherState: String] = [.running: "●", .waiting: "◉", .passed: "✓", .failed: "⚠", .finished: "✓", .lostTouch: "○"]
    static let markColors: [WatcherState: String] = [.running: "#38bdf8", .waiting: "#fbbf24", .passed: "#4ade80", .failed: "#f87171", .finished: "#9ca3af", .lostTouch: "#6b7280"]

    static func elapsedWords(_ ms: Double) -> String {
        let s = Int((max(0, ms) / 1000).rounded(.down))
        if s < 60 { return "\(s)s" }
        if s < 3600 { return "\(s / 60)m" }
        return "\(s / 3600)h \(String(format: "%02d", (s % 3600) / 60))m"
    }

    static func liveWatchers(_ file: BandFile?, _ now: Double) -> [Watcher] {
        guard let file, let ws = file.watchers, now - file.writtenAt <= staleMS else { return [] }
        return ws
    }

    struct WatcherRow: Equatable {
        var state: WatcherState
        var mark: String
        var color: String
        var titleColor: String?
        var isDim: Bool
        var text: String
        var elapsed: String?
        var progress: String?
    }

    static func watcherRows(_ watchers: [Watcher], _ now: Double, wide: Bool, motion: Bool) -> [WatcherRow] {
        watchers.map { w in
            let state = w.state
            var color = markColors[state]!
            var titleColor: String?
            var isDim = state == .lostTouch || state == .finished
            var ended: Double?
            if case .agent(_, _, _, _, _, _, let e) = w { ended = e }
            if state == .waiting {
                let glow = motion ? glowLevel(pulseProgress(now)) : 0
                color = mix(markColors[.waiting]!, white, glow)
                titleColor = glow > 0.01 ? mix(markColors[.waiting]!, white, glow) : markColors[.waiting]!
            } else if state == .failed {
                titleColor = markColors[.failed]!
            } else if state == .passed || state == .finished, let ended, motion {
                let t = max(0, min(1, (now - ended - 1000) / (fadeMS - 1000)))
                color = mix(markColors[state]!, "#4b5563", t)
                isDim = isDim || t >= 0.5
            }
            switch w {
            case .automatic(let kind, _):
                return WatcherRow(state: state, mark: marks[state]!, color: color, titleColor: titleColor, isDim: isDim,
                                  text: "background \(kind)", elapsed: nil, progress: nil)
            case let .agent(title, short, _, done, total, startedAt, endedAt):
                return WatcherRow(state: state, mark: marks[state]!, color: color, titleColor: titleColor, isDim: isDim,
                                  text: wide || short == nil ? title : short!,
                                  elapsed: startedAt.map { elapsedWords((endedAt ?? now) - $0) },
                                  progress: total.map { "\(done ?? 0)/\($0)" })
            }
        }
    }

    // MARK: The frame (draw.ts)

    private static func cells(_ runs: [Run], _ look: Look?, bold: Bool = false, dimBare: Bool) -> [Cell] {
        runs.map { r in
            Cell(text: r.text, color: r.color, bold: bold || look?.bold == true, italic: look?.italic == true,
                 underline: look?.underline == true, dim: look?.dim == true || (r.color == nil && dimBare))
        }
    }

    private static func plainCell(_ text: String, dim: Bool, color: String? = nil) -> Cell {
        Cell(text: text, color: color, dim: dim)
    }

    private static func styled(_ text: String, _ look: Look?, t: Double?, glow: Double) -> [Run] {
        let chars = letters(text, look?.font)
        let base: [String?] = if let look, !look.ink.isEmpty { gradient(look.ink, chars.count).map { Optional($0) } } else { chars.map { _ in nil } }
        return paint(chars, base, t, glow: glow)
    }

    static func isShimmering(_ m: Meter) -> Bool {
        m.resetLeft.map { $0 <= shimmerBeforeMS } ?? false
    }

    struct MeterDraw { var barWidth: Int; var plain: Bool; var lit: Bool; var full: Bool; var now: Double; var sweepStart: Double? }

    static func meterPieces(_ m: Meter, _ file: BandFile?, _ d: MeterDraw) -> [[Cell]] {
        let look = m.key == session ? file?.styles["session"] : m.key == weekly ? file?.styles["weekly"] : nil
        if m.isCrossed {
            return [cells(styled(m.name, look, t: nil, glow: 0), look, dimBare: true), [plainCell("reset", dim: true)]]
        }
        let name = cells(styled(m.name, look, t: nil, glow: 0), look, dimBare: d.plain)
        let tone = m.isWarning ? warnRed : m.color
        let barCells: [(ch: String, color: String?)] = bar(m.pct, m.fraction, d.barWidth).map { c in
            (c.ch, d.plain ? nil : c.part == .fill ? tone : c.part == .pace ? paceColor : nil)
        }
        let pctChars = letters("\(m.pct)%", look?.font)
        let span = barCells.count + pctChars.count
        let sweep = d.lit ? onceProgress(d.sweepStart, d.now) : nil
        let glow = d.lit && m.isWarning ? glowLevel(pulseProgress(d.now)) : 0
        let barRuns = paint(barCells.map(\.ch), barCells.map(\.color), sweep, glow: 0, offset: 0, span: span)
        let pctBase: [String?]
        if d.plain {
            pctBase = pctChars.map { _ in nil }
        } else if let look, !look.ink.isEmpty {
            pctBase = gradient(look.ink, pctChars.count).map { Optional($0) }
        } else {
            pctBase = pctChars.map { _ in tone }
        }
        let pctRuns = paint(pctChars, pctBase, sweep, glow: glow, offset: barCells.count, span: span)
        let resets = file?.styles["resets"]
        let shimmer = d.lit && isShimmering(m) ? pulseProgress(d.now) : nil
        var pieces: [[Cell]] = [
            name,
            [plainCell("▕", dim: true)] + barRuns.map { plainCell($0.text, dim: d.plain || $0.color == nil, color: $0.color) } + [plainCell("▏", dim: true)],
            cells(pctRuns, look, bold: m.isWarning, dimBare: d.plain),
        ]
        if m.isWarning { pieces.append([plainCell("⚠", dim: false, color: mix(warnRed, white, glow))]) }
        if d.full, let pace = m.pace { pieces.append([plainCell(pace, dim: true)]) }
        if let reset = m.reset { pieces.append(cells(styled(reset, resets, t: shimmer, glow: 0), resets, dimBare: true)) }
        return pieces
    }

    static func watcherPieces(_ r: WatcherRow, more: Int) -> [[Cell]] {
        var pieces: [[Cell]] = [[plainCell(r.mark, dim: false, color: r.color)], [plainCell(r.text, dim: r.isDim, color: r.titleColor)]]
        if let e = r.elapsed { pieces.append([plainCell(e, dim: true)]) }
        if let p = r.progress { pieces.append([plainCell(p, dim: r.isDim)]) }
        if more > 0 { pieces.append([plainCell("+\(more) more", dim: true)]) }
        return pieces
    }

    private static func joined(_ groups: [[Cell]], gap n: Int) -> [Cell] {
        groups.enumerated().flatMap { k, g in k == 0 ? g : [plainCell(String(repeating: " ", count: n), dim: false)] + g }
    }

    /// The frame as lines of cells, with the gaps the mod's Box layout puts between pieces.
    static func lines(_ snap: Snapshot?, _ file: BandFile?, _ i: Input) -> [[Cell]] {
        let full = bandFor(snap, now: i.now, bars: i.bars, style: .full, tz: i.tzOffsetMinutes, seat: i.seat)
        var fullFits = true
        if case let .meters(ms, _, _) = full { fullFits = ms.count <= i.maxRows && i.width >= fullColumns }
        let shown: Style = i.style == .full && fullFits ? .full : .compact
        let band = shown == .full ? full : bandFor(snap, now: i.now, bars: i.bars, style: .compact, tz: i.tzOffsetMinutes, seat: i.seat)
        let used: Int
        switch band {
        case .none: used = 0
        case .meters(let ms, _, _) where shown == .full: used = ms.count
        default: used = 1
        }
        let room = max(0, min(maxRows, i.maxRows - used))
        let rows = watcherRows(liveWatchers(file, i.now), i.now, wide: i.width >= wideRow, motion: i.moving)
        let visible = Array(rows.prefix(room))

        var out: [[Cell]] = []
        switch band {
        case .none:
            break
        case .line(let text):
            out.append([plainCell(text, dim: true)])
        case let .meters(ms, isDim, note):
            let d = MeterDraw(barWidth: shown == .compact && i.width < narrowColumns ? narrowBar : barWidth[shown]!,
                              plain: isDim, lit: i.moving && !isDim, full: shown == .full, now: i.now, sweepStart: nil)
            let meterRows = ms.map { m -> [Cell] in
                var dd = d
                dd.sweepStart = i.sweeps[m.key]
                return joined(meterPieces(m, file, dd), gap: 1)
            }
            let head = i.label.isEmpty ? nil : [plainCell(i.label, dim: true)]
            let tail = note.map { [plainCell($0, dim: true)] }
            if shown == .compact {
                out.append(joined([head].compactMap { $0 } + meterRows + [tail].compactMap { $0 }, gap: 3))
            } else {
                for (k, r) in meterRows.enumerated() {
                    var groups: [[Cell]] = []
                    if k == 0, let head { groups.append(head) }
                    groups.append(r)
                    if k == meterRows.count - 1, let tail { groups.append(tail) }
                    out.append(joined(groups, gap: 1))
                }
            }
        }
        for (n, r) in visible.enumerated() {
            out.append(joined(watcherPieces(r, more: n == visible.count - 1 ? rows.count - visible.count : 0), gap: 1))
        }
        return out
    }
}
