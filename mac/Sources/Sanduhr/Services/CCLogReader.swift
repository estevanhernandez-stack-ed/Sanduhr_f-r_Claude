import Foundation

/// The two token fields that count as burn against the subscription: input plus output. Cache
/// creation and cache reads are left out (billed differently), as on Windows (`TokenUsage`).
struct CCUsageEvent: Equatable, Sendable {
    let timestamp: Date?
    let model: String?
    let inputTokens: Int64
    let outputTokens: Int64
    let cwd: String?
    let skill: String?

    var tokens: Int64 { inputTokens + outputTokens }
}

/// A local calendar day, as the reader groups by it (Windows `DateOnly`).
struct CCLocalDay: Hashable, Comparable, Sendable {
    let year: Int
    let month: Int
    let day: Int

    init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    init(_ date: Date, calendar: Calendar) {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        self.init(year: c.year ?? 0, month: c.month ?? 0, day: c.day ?? 0)
    }

    /// Local midnight at the start of the day.
    func start(in calendar: Calendar) -> Date? {
        calendar.date(from: DateComponents(year: year, month: month, day: day))
    }

    static func < (a: CCLocalDay, b: CCLocalDay) -> Bool {
        (a.year, a.month, a.day) < (b.year, b.month, b.day)
    }
}

/// One Claude Code home's burn for one local day (Windows `RootDayBurn`): the total, by project
/// display name (`(unknown)` without a cwd) and by Sanduhr tier key. Unmapped models count toward
/// the total only.
struct CCDayBurn: Equatable, Sendable {
    var total: Int64 = 0
    var byProject: [String: Int64] = [:]
    var byTier: [String: Int64] = [:]
}

/// One local day of a Claude Code home as the live reader sees it (`CCLogReader.days`): the
/// total, its input and output, and the tokens by raw cwd (`""` without one), skill and raw model.
/// Held in memory for the Claude Usage page only; never stored.
struct CCLiveDay: Equatable, Sendable {
    var total: Int64 = 0
    var input: Int64 = 0
    var output: Int64 = 0
    var byCwd: [String: Int64] = [:]
    var bySkill: [String: Int64] = [:]
    var byModel: [String: Int64] = [:]
}

/// What one pass over the logs since a moment found: the badge's numbers.
struct CCBurnSince: Equatable, Sendable {
    /// Tokens by raw model string.
    var byModel: [String: Int64] = [:]
    /// Tokens by Sanduhr tier key (`TierForModel`); unmapped models are left out here.
    var byTier: [String: Int64] = [:]
    /// Every counted token, mapped or not.
    var total: Int64 = 0
    /// The events counted.
    var events = 0
}

// MARK: File access

/// A file's size and modification time, the reader's change test.
struct CCLogFileStat: Equatable, Sendable {
    let size: UInt64
    let modified: Date
}

/// Every way the reader touches the disk, injected so tests can count each call and prove that
/// nothing is touched when activity is off.
protocol CCLogFileSystem: Sendable {
    /// Whether `path` is a directory.
    func isDirectory(_ path: String) -> Bool
    /// The directories directly inside `path`, as absolute paths.
    func subdirectories(of path: String) -> [String]
    /// Every `*.jsonl` file below `path`, at any depth, as absolute paths.
    func jsonlFiles(under path: String) -> [String]
    /// Size and modification time, nil when the file is gone or unreadable.
    func stat(_ path: String) -> CCLogFileStat?
    /// The bytes from `offset` to the end, nil when the file can't be opened. The only call that
    /// opens a log.
    func read(_ path: String, from offset: UInt64) -> Data?
}

/// The real disk, through FileManager and FileHandle.
struct LocalCCLogFileSystem: CCLogFileSystem {
    func isDirectory(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
    }

    func subdirectories(of path: String) -> [String] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: path)) ?? []
        return names.sorted()
            .map { (path as NSString).appendingPathComponent($0) }
            .filter(isDirectory)
    }

    func jsonlFiles(under path: String) -> [String] {
        guard let walker = FileManager.default.enumerator(atPath: path) else { return [] }
        var out: [String] = []
        while let rel = walker.nextObject() as? String {
            guard rel.hasSuffix(".jsonl") else { continue }
            let full = (path as NSString).appendingPathComponent(rel)
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: full, isDirectory: &isDir), !isDir.boolValue {
                out.append(full)
            }
        }
        return out.sorted()
    }

    func stat(_ path: String) -> CCLogFileStat? {
        guard let a = try? FileManager.default.attributesOfItem(atPath: path),
              let size = (a[.size] as? NSNumber)?.uint64Value,
              let modified = a[.modificationDate] as? Date else { return nil }
        return CCLogFileStat(size: size, modified: modified)
    }

    func read(_ path: String, from offset: UInt64) -> Data? {
        guard let h = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? h.close() }
        do {
            try h.seek(toOffset: offset)
            return try h.readToEnd() ?? Data()
        } catch {
            return nil
        }
    }
}

// MARK: Reader

/// The Claude Code session-log reader for one Claude Code home (the folder an account links),
/// a port of Windows `CcLogReader` (itself 1:1 with the Python `cc_logs.py`).
///
/// **What is read.** `<home>/projects/<project>/**/*.jsonl`, subagent and workflow transcripts in
/// nested folders included; a missing `projects` folder is a fresh install, not an error. Only
/// `type: "assistant"` lines count; from each the timestamp, `message.model`,
/// `message.usage.input_tokens` and `.output_tokens`, `cwd` and `attributionSkill` are kept, and
/// nothing else (never message content). Malformed lines are skipped. Like Windows there is no
/// dedupe: every assistant line with tokens counts.
///
/// **Incremental.** Each file's parsed events are cached with its size, modification time and
/// the byte offset read up to (the last complete line). A file that has not changed is not
/// opened; a file that grew is read from that offset only, after checking the 64 bytes before it
/// still match (otherwise it was rewritten and is read whole); a shrunk file is read whole. The
/// last line, while it has no newline yet, is parsed every time and never kept, so a line being
/// written is counted once it is whole, and never twice. Files older than a query's cutoff are
/// not opened at all (the Windows mtime prefilter). Thread-safe: one query at a time.
final class CCLogReader: @unchecked Sendable {
    let root: String
    private let fs: CCLogFileSystem
    private let lock = NSLock()
    private var cache: [String: FileState] = [:]
    /// Model, cwd and skill strings repeat on every line: one copy each.
    private var strings: Set<String> = []

    init(root: String, fileSystem: CCLogFileSystem = LocalCCLogFileSystem()) {
        self.root = root
        self.fs = fileSystem
    }

    private struct FileState {
        var stat: CCLogFileStat
        /// Bytes consumed: the end of the last complete line.
        var offset: UInt64
        /// Up to `guardLength` bytes just before `offset`, to notice a rewrite.
        var guardBytes: Data
        /// Events from complete lines.
        var events: [CCUsageEvent]
        /// Events from the unterminated last line, re-read each time the file changes.
        var tail: [CCUsageEvent]

        var all: [CCUsageEvent] { tail.isEmpty ? events : events + tail }
    }

    static let guardLength: UInt64 = 64

    // MARK: Discovery

    /// Every session JSONL under the home's `projects` folder, nested ones included; files lying
    /// directly in `projects` are not sessions (Windows walks the project folders only).
    func discoverLogFiles() -> [String] {
        let projects = (root as NSString).appendingPathComponent("projects")
        guard fs.isDirectory(projects) else { return [] }
        var files: [String] = []
        for dir in fs.subdirectories(of: projects) {
            files.append(contentsOf: fs.jsonlFiles(under: dir))
        }
        return files
    }

    // MARK: Events

    /// The usage events in one file, through the cache. An unreadable file yields nothing.
    func usageEvents(in path: String) -> [CCUsageEvent] {
        lock.lock()
        defer { lock.unlock() }
        guard let st = fs.stat(path) else {
            cache.removeValue(forKey: path)
            return []
        }
        return load(path, stat: st)
    }

    /// The events of every file modified at or after `cutoff`, `body` called once per event.
    /// Drops cached files that are gone.
    private func forEachEvent(modifiedSince cutoff: Date?, _ body: (CCUsageEvent) -> Void) {
        lock.lock()
        defer { lock.unlock() }
        let files = discoverLogFiles()
        let present = Set(files)
        for key in cache.keys where !present.contains(key) { cache.removeValue(forKey: key) }
        for path in files {
            guard let st = fs.stat(path) else { continue }
            if let cutoff, st.modified < cutoff { continue }
            for ev in load(path, stat: st) { body(ev) }
        }
    }

    /// Cached events, reading only what changed. Called with the lock held.
    private func load(_ path: String, stat st: CCLogFileStat) -> [CCUsageEvent] {
        if let c = cache[path], c.stat == st { return c.all }
        if var c = cache[path], c.offset > 0, st.size >= c.offset {
            let back = c.guardBytes.count
            let start = c.offset - UInt64(back)
            guard let data = fs.read(path, from: start) else {
                cache.removeValue(forKey: path)
                return []
            }
            if data.count >= back, data.prefix(back) == c.guardBytes {
                let parsed = parse(data.dropFirst(back))
                c.events.append(contentsOf: parsed.events)
                c.tail = parsed.tail
                Self.settle(&c, data: data, dataStart: start, consumedEnd: back + parsed.consumed)
                c.stat = st
                cache[path] = c
                return c.all
            }
        }
        // New, shrunk or rewritten: the whole file.
        guard let data = fs.read(path, from: 0) else {
            cache.removeValue(forKey: path)
            return []
        }
        let parsed = parse(data[...])
        var c = FileState(stat: st, offset: 0, guardBytes: Data(), events: parsed.events, tail: parsed.tail)
        Self.settle(&c, data: data, dataStart: 0, consumedEnd: parsed.consumed)
        cache[path] = c
        return c.all
    }

    /// `data` was read from file position `dataStart` and its first `consumedEnd` bytes end on a
    /// newline: the offset moves there and the guard becomes the bytes just before it.
    private static func settle(_ c: inout FileState, data: Data, dataStart: UInt64, consumedEnd: Int) {
        c.offset = dataStart + UInt64(consumedEnd)
        let g = Int(min(guardLength, c.offset))
        c.guardBytes = Data(data.prefix(consumedEnd).suffix(g))
    }

    private struct Parsed {
        var events: [CCUsageEvent] = []
        var tail: [CCUsageEvent] = []
        /// Bytes up to and including the last newline.
        var consumed = 0
    }

    private func parse(_ bytes: Data.SubSequence) -> Parsed {
        var out = Parsed()
        var lineStart = bytes.startIndex
        var i = bytes.startIndex
        while i < bytes.endIndex {
            if bytes[i] == 0x0A {
                if let ev = event(bytes[lineStart..<i]) { out.events.append(ev) }
                lineStart = bytes.index(after: i)
            }
            i = bytes.index(after: i)
        }
        out.consumed = lineStart - bytes.startIndex
        if lineStart < bytes.endIndex, let ev = event(bytes[lineStart..<bytes.endIndex]) {
            out.tail.append(ev)
        }
        return out
    }

    private static let assistantMark = Data("\"assistant\"".utf8)
    private static let usageMark = Data("\"usage\"".utf8)

    /// One line as an event, or nil. A line without the bytes `"assistant"` and `"usage"` cannot
    /// be one and is not decoded (most lines are user turns and tool output).
    private func event(_ line: Data.SubSequence) -> CCUsageEvent? {
        guard line.range(of: Self.assistantMark) != nil, line.range(of: Self.usageMark) != nil,
              let obj = try? JSONSerialization.jsonObject(with: Data(line)) as? [String: Any] else { return nil }
        return Self.event(from: obj, intern: intern)
    }

    /// The Windows `Iterate` rules over a decoded line.
    static func event(from d: [String: Any], intern: (String?) -> String? = { $0 }) -> CCUsageEvent? {
        guard d["type"] as? String == "assistant",
              let msg = d["message"] as? [String: Any],
              let usage = msg["usage"] as? [String: Any] else { return nil }
        return CCUsageEvent(timestamp: (d["timestamp"] as? String).flatMap(CCTimestamp.parse),
                            model: intern(msg["model"] as? String),
                            inputTokens: number(usage["input_tokens"]),
                            outputTokens: number(usage["output_tokens"]),
                            cwd: intern(d["cwd"] as? String),
                            skill: intern(d["attributionSkill"] as? String))
    }

    /// A JSON number as Windows `Num` reads it: integers as is, fractions truncated, anything
    /// else (strings, booleans, null) zero.
    static func number(_ any: Any?) -> Int64 {
        guard let n = any as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return 0 }
        let d = n.doubleValue
        guard d.isFinite, abs(d) < 9.2e18 else { return 0 }
        return n.int64Value == Int64(d) ? n.int64Value : Int64(d)
    }

    private func intern(_ s: String?) -> String? {
        guard let s else { return nil }
        if let found = strings.firstIndex(of: s) { return strings[found] }
        strings.insert(s)
        return s
    }

    /// Forgets every cached file (the next query reads them whole).
    func invalidate() {
        lock.lock()
        cache.removeAll()
        strings.removeAll()
        lock.unlock()
    }

    // MARK: Queries

    /// Tokens by raw model string for events at or after `cutoff` (Windows `TokensSince`).
    /// Events without a timestamp or a model, and zero-token events, are left out.
    func tokensSince(_ cutoff: Date) -> [String: Int64] {
        burnSince(cutoff).byModel
    }

    /// `tokensSince` keyed by Sanduhr tier; unmapped models are dropped (`TokensSinceByTier`).
    func tokensSinceByTier(_ cutoff: Date) -> [String: Int64] {
        burnSince(cutoff).byTier
    }

    /// One pass since `cutoff`: by model, by tier, the total and the event count. The badge's
    /// source (Windows `RefreshCcDerivedAsync` over `TokensSince`).
    func burnSince(_ cutoff: Date) -> CCBurnSince {
        var out = CCBurnSince()
        forEachEvent(modifiedSince: cutoff) { ev in
            guard let ts = ev.timestamp, let model = ev.model, ts >= cutoff else { return }
            let t = ev.tokens
            guard t > 0 else { return }
            out.byModel[model, default: 0] += t
            out.total += t
            out.events += 1
            if let tier = Self.tierForModel(model) { out.byTier[tier, default: 0] += t }
        }
        return out
    }

    /// Tokens by working directory (raw cwd) at or after `since`; events without a cwd are left
    /// out (`TokensByProject`). Fold with `projectDisplayName` to show.
    func tokensByProject(since: Date) -> [String: Int64] {
        var out: [String: Int64] = [:]
        forEachEvent(modifiedSince: since) { ev in
            guard let ts = ev.timestamp, let cwd = ev.cwd, ts >= since, ev.tokens > 0 else { return }
            out[cwd, default: 0] += ev.tokens
        }
        return out
    }

    /// Tokens by `attributionSkill` at or after `since` (`TokensBySkill`).
    func tokensBySkill(since: Date) -> [String: Int64] {
        var out: [String: Int64] = [:]
        forEachEvent(modifiedSince: since) { ev in
            guard let ts = ev.timestamp, let skill = ev.skill, !skill.isEmpty, ts >= since,
                  ev.tokens > 0 else { return }
            out[skill, default: 0] += ev.tokens
        }
        return out
    }

    /// Tokens by local calendar day for the trailing `daysBack` days (`TokensByDay`).
    func tokensByDay(daysBack: Int = 30, now: Date = Date(), calendar: Calendar = .current) -> [CCLocalDay: Int64] {
        let cutoff = now.addingTimeInterval(-Double(daysBack) * 86_400)
        var out: [CCLocalDay: Int64] = [:]
        forEachEvent(modifiedSince: cutoff) { ev in
            guard let ts = ev.timestamp, ts >= cutoff, ev.tokens > 0 else { return }
            out[CCLocalDay(ts, calendar: calendar), default: 0] += ev.tokens
        }
        return out
    }

    /// One local day's burn (`BurnForLocalDay`): files modified since that day's midnight, events
    /// whose local day it is; projects by display name, `(unknown)` without a cwd; tiers for
    /// mapped models only.
    func burnForLocalDay(_ day: CCLocalDay, calendar: Calendar = .current,
                         dirHoldsGit: ((String) -> Bool)? = nil) -> CCDayBurn {
        var out = CCDayBurn()
        let midnight = day.start(in: calendar)
        forEachEvent(modifiedSince: midnight) { ev in
            guard let ts = ev.timestamp, CCLocalDay(ts, calendar: calendar) == day else { return }
            let t = ev.tokens
            guard t > 0 else { return }
            out.total += t
            let project: String
            if let cwd = ev.cwd, !cwd.isEmpty {
                project = dirHoldsGit.map { CCProjectName.displayName(cwd, dirHoldsGit: $0) }
                    ?? CCProjectName.displayName(cwd)
            } else {
                project = "(unknown)"
            }
            out.byProject[project, default: 0] += t
            if let tier = Self.tierForModel(ev.model) { out.byTier[tier, default: 0] += t }
        }
        return out
    }

    /// Every local day from `from` on, in one pass (the Claude Usage page's live source, Windows
    /// `AggregateForLocalCcTab` kept per day so live days compose with the record's closed days):
    /// files modified since `from`'s midnight, events with a timestamp and tokens. Projects stay
    /// raw cwds (`""` without one) for the page to name under the account's choice.
    func days(from: CCLocalDay, calendar: Calendar = .current) -> [CCLocalDay: CCLiveDay] {
        var out: [CCLocalDay: CCLiveDay] = [:]
        forEachEvent(modifiedSince: from.start(in: calendar)) { ev in
            guard let ts = ev.timestamp else { return }
            let t = ev.tokens
            guard t > 0 else { return }
            let day = CCLocalDay(ts, calendar: calendar)
            guard day >= from else { return }
            var d = out[day] ?? CCLiveDay()
            d.total += t
            d.input += ev.inputTokens
            d.output += ev.outputTokens
            d.byCwd[ev.cwd ?? "", default: 0] += t
            if let skill = ev.skill, !skill.isEmpty { d.bySkill[skill, default: 0] += t }
            let model = ev.model.flatMap { $0.isEmpty ? nil : $0 } ?? "<none>"
            d.byModel[model, default: 0] += t
            out[day] = d
        }
        return out
    }

    // MARK: Tiers

    /// More specific prefixes first (`_MODEL_TIER_PREFIXES`). Haiku has no tier of its own and
    /// folds into the weekly all-models one.
    static let modelTierPrefixes: [(prefix: String, tier: String)] = [
        ("claude-opus", Tier.sevenDayOpus.rawValue),
        ("claude-sonnet", Tier.sevenDaySonnet.rawValue),
        ("claude-fable", "seven_day_fable"),
        ("claude-haiku", Tier.sevenDay.rawValue),
    ]

    /// The Sanduhr tier key for a raw model string, nil for unknown models (`TierForModel`).
    static func tierForModel(_ model: String?) -> String? {
        guard let model, !model.isEmpty else { return nil }
        return modelTierPrefixes.first { model.hasPrefix($0.prefix) }?.tier
    }
}

// MARK: Timestamps

/// ISO 8601 timestamps as Claude Code writes them (`2026-05-05T10:00:00.123Z`), and as .NET's
/// round-trip format does (seven fraction digits, `+00:00`). No zone means UTC, as Windows
/// assumes. Hand-parsed: a formatter per line is the slow part of a large log.
enum CCTimestamp {
    static func parse(_ s: String) -> Date? {
        let b = Array(s.utf8)
        var i = 0
        func digits(_ n: Int) -> Int? {
            guard i + n <= b.count else { return nil }
            var v = 0
            for k in 0..<n {
                let c = b[i + k]
                guard c >= 48, c <= 57 else { return nil }
                v = v * 10 + Int(c - 48)
            }
            i += n
            return v
        }
        func expect(_ c: UInt8) -> Bool {
            guard i < b.count, b[i] == c else { return false }
            i += 1
            return true
        }
        guard let year = digits(4), expect(45), let month = digits(2), expect(45), let day = digits(2),
              (1...12).contains(month), (1...31).contains(day) else { return nil }
        var hour = 0, minute = 0, second = 0
        var fraction = 0.0
        if i < b.count, b[i] == 84 || b[i] == 116 || b[i] == 32 {   // T, t or a space
            i += 1
            guard let h = digits(2), expect(58), let m = digits(2) else { return nil }
            hour = h
            minute = m
            if expect(58) {
                guard let sec = digits(2) else { return nil }
                second = sec
                if i < b.count, b[i] == 46 || b[i] == 44 {   // . or ,
                    i += 1
                    var scale = 0.1
                    var any = false
                    while i < b.count, b[i] >= 48, b[i] <= 57 {
                        fraction += Double(b[i] - 48) * scale
                        scale /= 10
                        i += 1
                        any = true
                    }
                    guard any else { return nil }
                }
            }
            guard hour < 24, minute < 60, second < 61 else { return nil }
        }
        var offset = 0
        if i < b.count {
            let c = b[i]
            if c == 90 || c == 122 {                 // Z
                i += 1
            } else if c == 43 || c == 45 {           // + or -
                i += 1
                guard let oh = digits(2) else { return nil }
                _ = expect(58)
                guard let om = digits(2) else { return nil }
                offset = (oh * 3600 + om * 60) * (c == 45 ? -1 : 1)
            } else {
                return nil
            }
        }
        guard i == b.count else { return nil }
        let days = daysFromCivil(year, month, day)
        let secs = Double(days) * 86_400 + Double(hour * 3600 + minute * 60 + second) + fraction - Double(offset)
        return Date(timeIntervalSince1970: secs)
    }

    /// Days since 1970-01-01 for a proleptic Gregorian date (Howard Hinnant's algorithm).
    static func daysFromCivil(_ y0: Int, _ m: Int, _ d: Int) -> Int {
        let y = m <= 2 ? y0 - 1 : y0
        let era = (y >= 0 ? y : y - 399) / 400
        let yoe = y - era * 400
        let mp = m > 2 ? m - 3 : m + 9
        let doy = (153 * mp + 2) / 5 + d - 1
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
        return era * 146_097 + doe - 719_468
    }
}

// MARK: Project names

/// The project a cwd belongs to (Windows `ProjectDisplayName`), three rules in order:
/// 1. A cwd under a worktree folder (`<repo>/.claude/worktrees/x`, `<repo>/.worktrees/x`, or
///    deeper) names the repo; the outermost marker wins.
/// 2. Otherwise the nearest ancestor holding a `.git` entry (a folder, or a file in a linked
///    worktree or submodule) names it, so a session in a subfolder is the repo's.
/// 3. Otherwise the basename.
/// Both separators are handled. Names never leave the app in this item: only the badge shows.
enum CCProjectName {
    static let worktreeMarkers: [[String]] = [[".claude", "worktrees"], [".worktrees"]]

    private static let cacheLock = NSLock()
    nonisolated(unsafe) private static var cache: [String: String] = [:]

    /// With the real `.git` probe, memoized per cwd.
    static func displayName(_ cwd: String) -> String {
        guard !cwd.isEmpty else { return "" }
        cacheLock.lock()
        if let hit = cache[cwd] {
            cacheLock.unlock()
            return hit
        }
        cacheLock.unlock()
        let name = displayName(cwd, dirHoldsGit: holdsGit)
        cacheLock.lock()
        cache[cwd] = name
        cacheLock.unlock()
        return name
    }

    /// Whether `dir` holds a `.git` folder or file. Any failure answers no.
    static func holdsGit(_ dir: String) -> Bool {
        FileManager.default.fileExists(atPath: dir + "/.git")
    }

    /// The rules with the repo probe injected, so tests say which folders are repos.
    static func displayName(_ cwd: String, dirHoldsGit: (String) -> Bool) -> String {
        guard !cwd.isEmpty else { return "" }
        var s = cwd.replacingOccurrences(of: "\\", with: "/")
        while s.hasSuffix("/") { s.removeLast() }
        let parts = s.components(separatedBy: "/")
        if let repo = worktreeParentIndex(parts), !parts[repo].isEmpty { return parts[repo] }
        // Nearest enclosing repo wins; never the root segment ("" or "C:").
        var depth = parts.count
        while depth > 1 {
            let name = parts[depth - 1]
            if !name.isEmpty, dirHoldsGit(parts[0..<depth].joined(separator: "/")) { return name }
            depth -= 1
        }
        let last = parts.last ?? cwd
        return last.isEmpty ? cwd : last
    }

    /// The repo segment's index when a worktree marker is followed by at least one segment.
    private static func worktreeParentIndex(_ parts: [String]) -> Int? {
        guard parts.count > 1 else { return nil }
        for i in 1..<parts.count {
            for marker in worktreeMarkers {
                if i + marker.count >= parts.count { continue }
                var match = true
                for (k, m) in marker.enumerated() where parts[i + k].lowercased() != m {
                    match = false
                    break
                }
                if match { return i - 1 }
            }
        }
        return nil
    }
}

// MARK: Badge text

/// Compact token counts (Windows `TokenFormat.Compact`, Python `_format_tokens_compact`):
/// `999`, `1.5k`, `12k`, `1.2M`.
enum TokenFormat {
    static func compact(_ n: Int64) -> String {
        if n >= 1_000_000 { return String(format: "%.1fM", Double(n) / 1_000_000) }
        if n >= 10_000 { return "\(n / 1_000)k" }
        if n >= 1_000 { return String(format: "%.1fk", Double(n) / 1_000) }
        return "\(n)"
    }

    /// The badge as VoiceOver says it: "plus 1.5 thousand local tokens since the last refresh".
    static func spoken(_ n: Int64) -> String {
        let amount: String
        if n >= 1_000_000 {
            amount = String(format: "%.1f million", Double(n) / 1_000_000)
        } else if n >= 10_000 {
            amount = "\(n / 1_000) thousand"
        } else if n >= 1_000 {
            amount = String(format: "%.1f thousand", Double(n) / 1_000)
        } else {
            amount = "\(n)"
        }
        let unit = n == 1 ? "token" : "tokens"
        return "plus \(amount) local \(unit) since the last refresh"
    }
}
