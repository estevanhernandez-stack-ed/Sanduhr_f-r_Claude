import Foundation

/// One local day of coding as growth counts it: the date, and the active time that day in seconds
/// (zero when the source only knows the day had activity, as the Claude Code session logs do).
struct CodingDay: Equatable, Sendable {
    let date: CCLocalDay
    let active: TimeInterval

    init(date: CCLocalDay, active: TimeInterval = 0) {
        self.date = date
        self.active = active
    }
}

extension CCLocalDay {
    /// `yyyy-MM-dd` for a real calendar date (four-digit year, two-digit month and day), else nil.
    static func parse(_ text: String?) -> CCLocalDay? {
        guard let text else { return nil }
        let b = Array(text.utf8)
        guard b.count == 10, b[4] == 0x2D, b[7] == 0x2D else { return nil }
        func num(_ r: Range<Int>) -> Int? {
            var n = 0
            for i in r {
                guard b[i] >= 0x30, b[i] <= 0x39 else { return nil }
                n = n * 10 + Int(b[i] - 0x30)
            }
            return n
        }
        guard let y = num(0..<4), let m = num(5..<7), let d = num(8..<10), y >= 1 else { return nil }
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        guard DateComponents(calendar: cal, year: y, month: m, day: d).isValidDate else { return nil }
        return CCLocalDay(year: y, month: m, day: d)
    }

    /// `yyyy-MM-dd`.
    var isoText: String { String(format: "%04d-%02d-%02d", year, month, day) }
}

/// The companion's growth (spec 4, Windows' CompanionGrowth.cs): a number from 0 to 1 computed from
/// distinct coding days, never from tokens or usage spent, so a user well under pace grows exactly
/// as fast as one at the cap. The curve is piecewise linear through (0 days, 0), (3, 0.10),
/// (10, 0.35), (30, 0.75), (60, 1): the hourglass pack's stage thresholds land on day 3, 10 and 30.
enum CompanionGrowth {
    /// The curve's knots: (coding days, growth).
    static let knots: [(days: Double, growth: Double)] = [(0, 0), (3, 0.10), (10, 0.35), (30, 0.75), (60, 1.0)]

    /// Growth after `codingDays` distinct coding days.
    static func fromDays(_ codingDays: Int) -> Double {
        if codingDays <= 0 { return 0 }
        for i in 1..<knots.count {
            let (d0, g0) = knots[i - 1], (d1, g1) = knots[i]
            if Double(codingDays) <= d1 {
                return round6(g0 + (Double(codingDays) - d0) / (d1 - d0) * (g1 - g0))
            }
        }
        return 1
    }

    /// The fewest coding days at which growth reaches `growth` (the curve's inverse, rounded up to a
    /// whole day). 0 for 0 or less; the last knot's days for 1 or more.
    static func daysFor(_ growth: Double) -> Int {
        guard growth > 0 else { return 0 }
        for i in 1..<knots.count {
            let (d0, g0) = knots[i - 1], (d1, g1) = knots[i]
            if growth <= g1 + 1e-9 {
                var days = Int((d0 + (growth - g0) / (g1 - g0) * (d1 - d0) - 1e-9).rounded(.up))
                // Rounding up from the inverse can land a hair short of the threshold the curve itself rounds to.
                while fromDays(days) + 1e-9 < growth { days += 1 }
                return days
            }
        }
        return Int(knots[knots.count - 1].days)
    }

    /// The stage shown at `growth`: the stage with the highest `min_growth` that growth has reached.
    /// The first stage's threshold is 0, so there is always one.
    static func stageIndex(_ minGrowth: [Double], _ growth: Double) -> Int {
        var at = 0
        for (i, m) in minGrowth.enumerated() where growth + 1e-9 >= m { at = i }
        return at
    }

    static func round6(_ x: Double) -> Double { (x * 1_000_000).rounded() / 1_000_000 }
}

/// Sanduhr Time's day files (`~/.sanduhr/time/days/<yyyy-mm-dd>.json`), read-only. A day file is v1
/// (`totals.claudeMs`) or v2 (`totals.agentsMs`); both carry `totals.totalMs`, the union of all time,
/// which is what a coding day is measured by (a file written without it is summed from its parts).
enum SanduhrTimeDays {
    /// A day file is at most this big; anything larger is skipped unread.
    static let maxFileBytes = 4 * 1024 * 1024

    /// `~/.sanduhr/time/days`, the shared folder other estate tools write. Never created here.
    static var standardDir: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".sanduhr", isDirectory: true)
            .appendingPathComponent("time", isDirectory: true)
            .appendingPathComponent("days", isDirectory: true)
    }

    /// The day one file describes, or nil when it is not a day file of version 1 or 2 with a date and some time.
    static func parse(_ json: Data) -> CodingDay? {
        guard let root = (try? JSONSerialization.jsonObject(with: json)) as? [String: Any] else { return nil }
        guard let v = number(root["v"]), v == 1 || v == 2 else { return nil }
        guard let date = CCLocalDay.parse(root["date"] as? String) else { return nil }
        guard let totals = root["totals"] as? [String: Any] else { return nil }
        var ms = msOf(totals["totalMs"])
        if ms <= 0 {
            ms = msOf(totals["youMs"]) + msOf(totals["bothMs"]) + msOf(totals[v == 1 ? "claudeMs" : "agentsMs"])
        }
        guard ms > 0 else { return nil }
        return CodingDay(date: date, active: min(ms, 86_400_000) / 1000)
    }

    /// A JSON number (not a boolean) as a finite Double.
    private static func number(_ any: Any?) -> Double? {
        guard let n = any as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { return nil }
        return n.doubleValue
    }

    private static func msOf(_ any: Any?) -> Double {
        if let d = number(any), d > 0 { return d }
        return 0
    }

    /// True when the folder holds at least one file named like a day file: Sanduhr Time is installed and has written.
    static func present(_ dir: URL) -> Bool {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []
        return names.contains { Self.isDayFileName($0) }
    }

    /// `????-??-??.json`, any characters in the question marks.
    static func isDayFileName(_ name: String) -> Bool {
        let c = Array(name)
        return c.count == 15 && name.hasSuffix(".json") && c[4] == "-" && c[7] == "-"
    }

    /// Every coding day in `dir`, one per date (the longest when a date appears twice), oldest first.
    /// Empty when the folder is absent or unreadable. Reads only files named like a date. This
    /// parses every file each call; `SanduhrTimeDaysReader` keeps what it parsed for the hourly caller.
    static func read(_ dir: URL) -> [CodingDay] {
        SanduhrTimeDaysReader().read(dir)
    }
}

/// `SanduhrTimeDays.read` with a memory: each day file is parsed once and again only when its size
/// or modification time changes, so the hourly refresh costs one directory listing and a stat per
/// file once the folder has been read. Thread-safe.
final class SanduhrTimeDaysReader: @unchecked Sendable {
    private struct Stamp: Equatable {
        let size: Int
        let modified: Date
    }

    private let lock = NSLock()
    private var cache: [String: (stamp: Stamp, day: CodingDay?)] = [:]
    /// How many files the last `read` had to open (a test hook for "cheap").
    private(set) var opened = 0

    func read(_ dir: URL) -> [CodingDay] {
        lock.lock(); defer { lock.unlock() }
        opened = 0
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else { cache = [:]; return [] }
        var byDate: [CCLocalDay: CodingDay] = [:]
        var seen = Set<String>()
        for name in names where name.hasSuffix(".json") {
            let stem = String(name.dropLast(5))
            guard CCLocalDay.parse(stem) != nil else { continue }
            let path = dir.appendingPathComponent(name).path
            guard let attrs = try? fm.attributesOfItem(atPath: path),
                  let size = (attrs[.size] as? NSNumber)?.intValue,
                  size <= SanduhrTimeDays.maxFileBytes
            else { continue }
            let stamp = Stamp(size: size, modified: attrs[.modificationDate] as? Date ?? .distantPast)
            seen.insert(name)
            let day: CodingDay?
            if let hit = cache[name], hit.stamp == stamp {
                day = hit.day
            } else {
                opened += 1
                day = (try? Data(contentsOf: URL(fileURLWithPath: path))).flatMap(SanduhrTimeDays.parse)
                cache[name] = (stamp, day)
            }
            if let day, (byDate[day.date].map { $0.active < day.active } ?? true) { byDate[day.date] = day }
        }
        for key in cache.keys where !seen.contains(key) { cache.removeValue(forKey: key) }
        return byDate.values.sorted { $0.date < $1.date }
    }
}

/// What the companion remembers between runs (`widgets/companion.json`, so Reset removes it with the
/// packs): the highest growth reached and every coding day seen so far. Days are kept because a
/// source forgets them (Claude Code's session logs are pruned after a month); growth is kept because
/// it never goes backwards. Nothing else: no usage, no tokens, no project names.
struct CompanionGrowthState: Equatable, Sendable {
    static let fileName = "companion.json"
    static let schemaVersion = 1
    /// Days kept at most (a decade of daily coding); the oldest go first. Growth stays where it was.
    static let maxDays = 3660
    static let empty = CompanionGrowthState(growth: 0, days: [], active: 0)

    var growth: Double
    var days: Set<CCLocalDay>
    /// Active coding time in seconds.
    var active: TimeInterval

    /// The state file inside a widgets root (`WidgetStore.root`).
    static func file(in widgetsRoot: URL) -> URL { widgetsRoot.appendingPathComponent(fileName) }

    /// The state after seeing `newDays`: the days joined to those remembered, the active time taken as
    /// the larger of what was kept and what the source now totals, and growth the larger of the kept
    /// value and the curve's.
    func observe(_ newDays: [CodingDay]) -> CompanionGrowthState {
        var all = days
        var sourceActive: TimeInterval = 0
        for d in newDays {
            all.insert(d.date)
            sourceActive += d.active
        }
        if all.count > Self.maxDays { all = Set(all.sorted(by: >).prefix(Self.maxDays)) }
        let g = max(growth, CompanionGrowth.fromDays(all.count))
        return CompanionGrowthState(growth: min(max(g, 0), 1), days: all, active: max(active, sourceActive))
    }

    static func parse(_ data: Data?) -> CompanionGrowthState {
        guard let data, let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return .empty }
        func num(_ any: Any?) -> Double? {
            guard let n = any as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { return nil }
            return n.doubleValue
        }
        let growth = min(max(num(root["growth"]) ?? 0, 0), 1)
        var days = Set<CCLocalDay>()
        for case let s as String in (root["days"] as? [Any]) ?? [] {
            if let d = CCLocalDay.parse(s) { days.insert(d) }
        }
        let activeMs = max(num(root["active_ms"]) ?? 0, 0)
        return CompanionGrowthState(growth: growth, days: days, active: activeMs / 1000)
    }

    func json() -> Data {
        var out = "{\n  \"schema_version\": \(Self.schemaVersion),\n"
        out += "  \"growth\": \(CompanionGrowth.round6(growth)),\n"
        out += "  \"active_ms\": \(Int64((active * 1000).rounded())),\n"
        if days.isEmpty {
            out += "  \"days\": []\n}\n"
        } else {
            out += "  \"days\": [\n" + days.sorted().map { "    \"\($0.isoText)\"" }.joined(separator: ",\n") + "\n  ]\n}\n"
        }
        return Data(out.utf8)
    }

    /// Reads the state from `file`; empty when it is absent or unreadable.
    static func load(_ file: URL) -> CompanionGrowthState {
        parse(try? Data(contentsOf: file))
    }

    /// Writes the state atomically. False when the folder could not be written, and after Reset
    /// Sanduhr… ran (nothing is put back).
    @discardableResult
    func save(_ file: URL) -> Bool {
        guard !ResetGate.isOn else { return false }
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            try json().write(to: file, options: .atomic)
            return true
        } catch {
            return false
        }
    }
}

/// The one call the companion makes to read growth, cheap enough to run once an hour (or on demand):
/// it picks the source, folds the days into the remembered state, and saves only when something
/// moved. Blocking work (a directory walk, maybe a log scan): call it off the main thread.
final class CompanionGrowthReader: @unchecked Sendable {
    /// Days with Claude Code activity, used when `~/.sanduhr/time/days` has no day files.
    typealias Fallback = @Sendable () -> [CodingDay]

    private let timeDir: URL
    private let stateFile: URL
    private let fallback: Fallback
    private let timeReader = SanduhrTimeDaysReader()

    init(timeDaysDir: URL = SanduhrTimeDays.standardDir, stateFile: URL, fallback: @escaping Fallback) {
        self.timeDir = timeDaysDir
        self.stateFile = stateFile
        self.fallback = fallback
    }

    /// The remembered state, read from disk.
    func loadState() -> CompanionGrowthState { .load(stateFile) }

    /// Reads the days from the active source, folds them into `was` (never lower) and saves the
    /// result when growth, days or active time changed.
    func refresh(from was: CompanionGrowthState) -> CompanionGrowthState {
        let days = SanduhrTimeDays.present(timeDir) ? timeReader.read(timeDir) : fallback()
        let next = was.observe(days)
        if next != was { next.save(stateFile) }
        return next
    }

    /// The fallback source: the local days with Claude Code activity in the last `daysBack` days
    /// across the linked homes. Dates only; the token counts are never used.
    static func claudeCodeDays(_ readers: [CCLogReader], daysBack: Int = 60, now: Date = Date(),
                               calendar: Calendar = .current) -> [CodingDay] {
        var days = Set<CCLocalDay>()
        for r in readers { days.formUnion(r.tokensByDay(daysBack: daysBack, now: now, calendar: calendar).keys) }
        return days.sorted().map { CodingDay(date: $0) }
    }
}
