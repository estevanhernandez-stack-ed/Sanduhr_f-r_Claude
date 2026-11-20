import Foundation

/// The Codex homes to read: `CODEX_HOME` first when the app's own environment has it, then
/// `~/.codex`, folders that exist only, each once. An app launched from the Dock does not see a
/// shell's exports, so a `CODEX_HOME` set in `.zshrc` is missed (agent sources design, section 1).
enum CodexHomes {
    static func discover(home: URL, environment: [String: String] = ProcessInfo.processInfo.environment,
                         fileManager: FileManager = .default) -> [URL] {
        var candidates: [URL] = []
        if let custom = environment["CODEX_HOME"]?.trimmingCharacters(in: .whitespaces), !custom.isEmpty {
            candidates.append(URL(fileURLWithPath: custom, isDirectory: true))
        }
        candidates.append(home.appendingPathComponent(CodexInstall.folderName, isDirectory: true))
        var found: [URL] = []
        var seen = Set<String>()
        for url in candidates {
            let std = url.standardizedFileURL
            var isDir: ObjCBool = false
            guard fileManager.fileExists(atPath: std.path, isDirectory: &isDir), isDir.boolValue else { continue }
            let key = std.path.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            guard seen.insert(key).inserted else { continue }
            found.append(std)
        }
        return found
    }

    /// The homes on this Mac, for the app.
    static func live() -> [URL] {
        discover(home: FileManager.default.homeDirectoryForCurrentUser)
    }
}

/// One Codex plan window from a rollout's `rate_limits`. `usedPercent` is 0 to 100, as Codex
/// writes it; `resetsAt` comes from epoch seconds.
struct CodexWindow: Equatable {
    var usedPercent: Double
    var windowMinutes: Int
    var resetsAt: Date?
}

/// The newest Codex limits Sanduhr could read, and when Codex wrote them (the event's timestamp).
struct CodexLimitsReading: Equatable {
    var asOf: Date
    var primary: CodexWindow?
    var secondary: CodexWindow?
    var planType: String?
}

/// Parses Codex's `token_count` rollout events and turns a reading into the tier shape Claude's
/// fetch produces. Ported from Windows' `CodexLimits.cs`: reads only percents, window lengths,
/// reset times, the event timestamp and the plan name; never message text, ids or paths.
enum CodexLimits {
    /// A reading is fresh for 30 minutes after its `as_of`.
    static let freshFor: TimeInterval = 30 * 60

    /// The window lengths Sanduhr can pace; any other is dropped.
    static func tier(forWindowMinutes minutes: Int) -> Tier? {
        switch minutes {
        case 300: return .fiveHour
        case 10080: return .sevenDay
        default: return nil
        }
    }

    // DateTimeOffset's range on Windows (years 1 to 9999), so an out-of-range reset reads as none there and here.
    private static let minEpoch: Int64 = -62_135_596_800
    private static let maxEpoch: Int64 = 253_402_300_799

    /// A reading from one rollout line, or nil for anything that is not a `token_count` event with at
    /// least one well-formed window and a timestamp.
    static func parse(line: Substring) -> CodexLimitsReading? {
        guard line.first == "{", line.contains("rate_limits") else { return nil }
        guard let data = line.data(using: .utf8),
              let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let payload = root["payload"] as? [String: Any],
              payload["type"] as? String == "token_count",
              let limits = payload["rate_limits"] as? [String: Any],
              let stamp = root["timestamp"] as? String,
              let asOf = ISO8601.parse(stamp) else { return nil }
        let primary = window(limits["primary"])
        let secondary = window(limits["secondary"])
        guard primary != nil || secondary != nil else { return nil }
        return CodexLimitsReading(asOf: asOf, primary: primary, secondary: secondary,
                                  planType: limits["plan_type"] as? String)
    }

    static func parse(line: String) -> CodexLimitsReading? { parse(line: Substring(line)) }

    private static func number(_ any: Any?) -> NSNumber? {
        guard let n = any as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return nil }
        return n
    }

    private static func window(_ any: Any?) -> CodexWindow? {
        guard let w = any as? [String: Any],
              let used = number(w["used_percent"])?.doubleValue, used.isFinite,
              let minutesNumber = number(w["window_minutes"]), !CFNumberIsFloatType(minutesNumber),
              let minutes = Int32(exactly: minutesNumber.int64Value) else { return nil }
        var resets: Date?
        if let r = number(w["resets_at"]), !CFNumberIsFloatType(r) {
            let epoch = r.int64Value
            if epoch >= minEpoch, epoch <= maxEpoch { resets = Date(timeIntervalSince1970: TimeInterval(epoch)) }
        }
        return CodexWindow(usedPercent: min(max(used, 0), 100), windowMinutes: Int(minutes), resetsAt: resets)
    }

    /// The tiers: windows mapped by length, not position. A window whose reset has passed reads 0%
    /// and keeps its reset time. An odd length is dropped and handed to `unknownWindow`.
    static func toUsage(_ reading: CodexLimitsReading, now: Date,
                        unknownWindow: (Int) -> Void = { _ in }) -> UsageResponse {
        var usage = UsageResponse()
        for w in [reading.primary, reading.secondary].compactMap({ $0 }) {
            guard let tier = tier(forWindowMinutes: w.windowMinutes) else {
                unknownWindow(w.windowMinutes)
                continue
            }
            usage.tiers[tier] = TierUsage(utilization: passed(w, now) ? 0 : w.usedPercent,
                                          resetsAt: w.resetsAt.map { ISO8601.stamp($0, fractional: false) })
        }
        return usage
    }

    /// Fresh within `freshFor` of the event, or once any window's reset has passed.
    static func isFresh(_ reading: CodexLimitsReading, now: Date) -> Bool {
        if now.timeIntervalSince(reading.asOf) <= freshFor { return true }
        return passed(reading.primary, now) || passed(reading.secondary, now)
    }

    /// The tiers whose number can be trusted at `now`: the event is recent, or that tier's own
    /// reset has passed (it then reads 0). Per tier, so an idle Codex whose five-hour window rolled
    /// over does not make a stale weekly percent look current.
    static func freshTiers(_ reading: CodexLimitsReading, now: Date) -> Set<Tier> {
        let recent = now.timeIntervalSince(reading.asOf) <= freshFor
        var set = Set<Tier>()
        for w in [reading.primary, reading.secondary].compactMap({ $0 }) {
            if let t = tier(forWindowMinutes: w.windowMinutes), recent || passed(w, now) { set.insert(t) }
        }
        return set
    }

    private static func passed(_ w: CodexWindow?, _ now: Date) -> Bool {
        guard let r = w?.resetsAt else { return false }
        return r <= now
    }
}

enum CodexReadStatus: Equatable {
    case notInstalled, noLimits, ok, unreadable
}

struct CodexReadResult: Equatable {
    var status: CodexReadStatus
    var reading: CodexLimitsReading?
}

/// A rollout as a listing found it: no file is opened to list it.
struct CodexRolloutFile: Equatable {
    var url: URL
    var size: UInt64
    var modified: Date
}

/// Every way the reader touches the disk, injected so tests can count each call and prove nothing
/// outside `sessions/` is ever opened.
protocol CodexFileSystem {
    func isDirectory(_ url: URL) -> Bool
    /// Every regular `rollout-*.jsonl` and `rollout-*.jsonl.zst` below `sessions`, at any depth.
    func rollouts(under sessions: URL) -> [CodexRolloutFile]
    /// `length` bytes from `offset`. Opens the file.
    func read(_ url: URL, offset: UInt64, length: Int) throws -> Data
    /// The whole file. Opens it.
    func readAll(_ url: URL) throws -> Data
}

/// The real disk.
struct CodexDisk: CodexFileSystem {
    func isDirectory(_ url: URL) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) && isDir.boolValue
    }

    static func isRolloutName(_ name: String) -> Bool {
        let lower = name.lowercased()
        return lower.hasPrefix("rollout-") && (lower.hasSuffix(".jsonl") || lower.hasSuffix(".jsonl.zst"))
    }

    func rollouts(under sessions: URL) -> [CodexRolloutFile] {
        let keys: [URLResourceKey] = [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]
        guard let walker = FileManager.default.enumerator(at: sessions, includingPropertiesForKeys: keys,
                                                          options: [], errorHandler: { _, _ in true }) else { return [] }
        var files: [CodexRolloutFile] = []
        for case let url as URL in walker {
            guard Self.isRolloutName(url.lastPathComponent),
                  let v = try? url.resourceValues(forKeys: Set(keys)), v.isRegularFile == true,
                  let size = v.fileSize, let modified = v.contentModificationDate else { continue }
            files.append(CodexRolloutFile(url: url, size: UInt64(size), modified: modified))
        }
        return files
    }

    func read(_ url: URL, offset: UInt64, length: Int) throws -> Data {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        try handle.seek(toOffset: offset)
        return try handle.read(upToCount: length) ?? Data()
    }

    func readAll(_ url: URL) throws -> Data {
        try Data(contentsOf: url, options: .mappedIfSafe)
    }
}

/// Per-file results kept between polls, keyed by path and checked against (size, mtime), so an
/// unchanged compressed rollout is not decompressed again and a corrupt one stays failed.
struct CodexReadCache {
    struct Entry: Equatable {
        var size: UInt64
        var modified: Date
        var reading: CodexLimitsReading?
        var bad: Bool
    }
    var entries: [String: Entry] = [:]
}

/// Finds the newest Codex limits in the rollouts under every Codex home (agent layer spec, section
/// 1; parity with Windows' `CodexLimitsReader.cs`). Reads nothing outside `sessions/`, never
/// throws, never logs a path or content.
///
/// File mtime is not trusted alone: Codex rewrites old rollouts when it compresses them, so the
/// candidates are the newest files by mtime and by the session stamp in the name, and the reading
/// with the newest event time wins. Plain `.jsonl` is read from its end in growing chunks up to
/// `maxTailBytes`; `.jsonl.zst` is decompressed whole under `zstdCap` and its last `maxTailBytes`
/// searched.
struct CodexLimitsReader {
    static let maxTailBytes = 4 * 1024 * 1024
    static let maxFiles = 20
    static let firstChunk = 64 * 1024

    var homes: [URL]
    var fileSystem: CodexFileSystem = CodexDisk()
    var zstdCap: Int = Zstd.defaultCap
    var maxTailBytes: Int = CodexLimitsReader.maxTailBytes
    var log: (String) -> Void = { _ in }

    func read() -> CodexReadResult {
        var cache = CodexReadCache()
        return read(cache: &cache)
    }

    func read(cache: inout CodexReadCache) -> CodexReadResult {
        guard !homes.isEmpty else { return CodexReadResult(status: .notInstalled) }
        var files: [CodexRolloutFile] = []
        for home in homes {
            let sessions = home.appendingPathComponent("sessions", isDirectory: true)
            guard fileSystem.isDirectory(sessions) else { continue }
            files += fileSystem.rollouts(under: sessions).filter { CodexDisk.isRolloutName($0.url.lastPathComponent) }
        }
        let live = Set(files.map(\.url.path))
        cache.entries = cache.entries.filter { live.contains($0.key) }
        guard !files.isEmpty else { return CodexReadResult(status: .noLimits) }

        let byMtime = files.sorted { $0.modified > $1.modified }.prefix(Self.maxFiles)
        let byName = files.sorted {
            let a = Self.nameStamp($0.url.lastPathComponent) ?? .distantPast
            let b = Self.nameStamp($1.url.lastPathComponent) ?? .distantPast
            return a != b ? a > b : $0.modified > $1.modified
        }.prefix(Self.maxFiles)
        var seen = Set<String>()
        let candidates = (Array(byMtime) + Array(byName)).filter { seen.insert($0.url.path).inserted }

        var tried = 0, failed = 0
        var best: CodexLimitsReading?
        for file in candidates {
            tried += 1
            let reading: CodexLimitsReading?
            if let hit = cache.entries[file.url.path], hit.size == file.size, hit.modified == file.modified {
                if hit.bad { failed += 1 }
                reading = hit.reading
            } else {
                do {
                    reading = try newest(in: file)
                    cache.entries[file.url.path] = .init(size: file.size, modified: file.modified, reading: reading, bad: false)
                } catch let e as Zstd.Failure {
                    // The same (path, size, mtime) fails the same way: remember it. IO errors are not cached.
                    failed += 1
                    cache.entries[file.url.path] = .init(size: file.size, modified: file.modified, reading: nil, bad: true)
                    log("codex: a rollout could not be read (\(Self.kind(e)))")
                    continue
                } catch {
                    failed += 1
                    log("codex: a rollout could not be read (io)")
                    continue
                }
            }
            if let r = reading, best.map({ r.asOf > $0.asOf }) ?? true { best = r }
        }
        if let best { return CodexReadResult(status: .ok, reading: best) }
        return CodexReadResult(status: tried > 0 && failed == tried ? .unreadable : .noLimits)
    }

    private static func kind(_ e: Zstd.Failure) -> String {
        switch e {
        case .capExceeded: return "over the size cap"
        case .badFrame: return "bad frame"
        case .truncated: return "truncated"
        }
    }

    private func newest(in file: CodexRolloutFile) throws -> CodexLimitsReading? {
        if file.url.pathExtension.lowercased() == "zst" {
            let packed = try fileSystem.readAll(file.url)
            let text = try Zstd.decompress(packed, cap: zstdCap)
            return Self.newest(in: text.suffix(maxTailBytes))
        }
        // Plain: from the end, doubling the window until a qualifying event or the tail cap.
        let size = Int(clamping: file.size)
        var window = min(Self.firstChunk, maxTailBytes)
        var buffer = Data()
        var start = size
        while true {
            let want = min(window, maxTailBytes, size)
            let newStart = size - want
            if newStart < start {
                let chunk = try fileSystem.read(file.url, offset: UInt64(newStart), length: start - newStart)
                buffer = chunk + buffer
                start = newStart
            }
            if let r = Self.newest(in: buffer) { return r }
            if start == 0 || want >= maxTailBytes { return nil }
            window *= 2
        }
    }

    /// The last qualifying event in `data`, scanning lines from the end. A torn or malformed line is skipped.
    static func newest(in data: Data) -> CodexLimitsReading? {
        let text = String(decoding: data, as: UTF8.self)
        for line in text.split(separator: "\n", omittingEmptySubsequences: true).reversed() {
            let trimmed = line.hasSuffix("\r") ? line.dropLast() : line
            if let r = CodexLimits.parse(line: trimmed) { return r }
        }
        return nil
    }

    private static let stampRegex = try! NSRegularExpression(
        pattern: #"^rollout-(\d{4})-(\d{2})-(\d{2})T(\d{2})-(\d{2})-(\d{2})"#)

    /// The session's start from a name like `rollout-2026-10-22T20-00-00-<id>.jsonl`, UTC, or nil.
    static func nameStamp(_ name: String) -> Date? {
        let ns = name as NSString
        guard let m = stampRegex.firstMatch(in: name, range: NSRange(location: 0, length: ns.length)) else { return nil }
        let parts = (1...6).compactMap { Int(ns.substring(with: m.range(at: $0))) }
        guard parts.count == 6 else { return nil }
        var c = DateComponents()
        c.year = parts[0]; c.month = parts[1]; c.day = parts[2]
        c.hour = parts[3]; c.minute = parts[4]; c.second = parts[5]
        guard c.month! >= 1, c.month! <= 12, c.day! >= 1, c.day! <= 31, c.hour! < 24, c.minute! < 60, c.second! < 60 else { return nil }
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        return cal.date(from: c)
    }
}
