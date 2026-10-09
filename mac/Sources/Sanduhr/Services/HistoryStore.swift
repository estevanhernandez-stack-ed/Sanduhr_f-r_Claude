import Foundation

/// Per-tier history of each account's meters: what the sparklines draw, and (item 43) 30 days of
/// it, as Windows keeps (`UsageHistory`). File format (JSON) is the Python and Windows one, so a
/// file moves between them unchanged:
///     { "five_hour": [ { "t": "2026-04-15T...", "v": 42 }, ... ], ... }
/// Windows may add `"resets_at"` to a point; it is kept when the file is rewritten.
enum HistoryStore {
    /// 30 days × 288 five-minute fetches, Windows' `UsageHistory.MaxHistory`.
    static let maxPoints = 8640
    /// Points older than this are dropped when the next reading is appended.
    static let retention: TimeInterval = 30 * 24 * 60 * 60
    /// How many recent points a sparkline draws: what the Mac kept before item 43 (24 five-minute
    /// fetches, about 2 hours), so the cards look as they did.
    static let sparklinePoints = 24

    /// Every change to the app's history files runs here, in order: a fetch's write (about 50 ms
    /// for a full 30-day file) stays off the main thread, and a rename, removal, erase or load
    /// waits for the writes queued before it.
    static let io = DispatchQueue(label: "com.626labs.sanduhr.history", qos: .utility)

    struct Point: Codable, Equatable {
        let t: String   // ISO-8601 timestamp
        let v: Double   // utilization 0–100
        /// Windows' optional reset instant; nil on every point the Mac writes.
        var resetsAt: String? = nil

        enum CodingKeys: String, CodingKey {
            case t, v
            case resetsAt = "resets_at"
        }
    }

    typealias History = [String: [Point]]     // keyed by Tier.rawValue

    /// One history file per account, `history.{label}.json` (Windows `Paths.HistoryFileFor`),
    /// in Application Support/Sanduhr. The legacy single file, `history.json`, is used only while
    /// there are no accounts, and becomes Personal's once (`adoptLegacy`).
    struct Files {
        let dir: URL

        static let standard: Files = {
            let fm = FileManager.default
            let base = (try? fm.url(for: .applicationSupportDirectory,
                                    in: .userDomainMask,
                                    appropriateFor: nil, create: true))
                ?? fm.homeDirectoryForCurrentUser
                    .appendingPathComponent("Library/Application Support")
            let dir = base.appendingPathComponent("Sanduhr", isDirectory: true)
            try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
            return Files(dir: dir)
        }()

        /// The account's file, or the legacy `history.json` for nil.
        func url(for account: String?) -> URL {
            dir.appendingPathComponent(account.map { "history.\($0).json" } ?? "history.json")
        }

        func load(_ account: String?) -> History {
            guard let data = try? Data(contentsOf: url(for: account)),
                  let h = try? JSONDecoder().decode(History.self, from: data)
            else { return [:] }
            return h
        }

        func save(_ h: History, account: String?) {
            guard let data = try? JSONEncoder().encode(h) else { return }
            try? data.write(to: url(for: account), options: .atomic)
        }

        /// Append a reading and trim (`trim`). Returns the updated series.
        @discardableResult
        func append(_ tier: Tier, utilization: Double, account: String?, now: Date = Date()) -> [Point] {
            append([(tier, utilization)], account: account, now: now)[tier.rawValue] ?? []
        }

        /// One fetch's readings, read and written once whatever the number of tiers. Returns the
        /// whole updated history.
        @discardableResult
        func append(_ readings: [(Tier, Double)], account: String?, now: Date = Date()) -> History {
            var h = load(account)
            guard !readings.isEmpty else { return h }
            let iso = ISO8601DateFormatter().string(from: now)
            let cutoff = now.addingTimeInterval(-retention)
            for (tier, utilization) in readings {
                var series = h[tier.rawValue] ?? []
                series.append(.init(t: iso, v: utilization))
                h[tier.rawValue] = trim(series, cutoff: cutoff)
            }
            save(h, account: account)
            return h
        }

        /// The upgrade's one-time rename of `history.json` to `history.{label}.json`, never over
        /// an existing file. True when it moved.
        @discardableResult
        func adoptLegacy(into label: String) -> Bool {
            HistoryStore.io.sync { adopt(into: label) }
        }

        private func adopt(into label: String) -> Bool {
            let fm = FileManager.default
            let legacy = url(for: nil), target = url(for: label)
            guard fm.fileExists(atPath: legacy.path), !fm.fileExists(atPath: target.path) else { return false }
            return (try? fm.moveItem(at: legacy, to: target)) != nil
        }

        /// The file follows a renamed account. A change of case alone goes through a temporary
        /// name, since the Mac's file system sees both names as one file.
        func rename(_ old: String, to new: String) {
            HistoryStore.io.sync { move(old, to: new) }
        }

        private func move(_ old: String, to new: String) {
            let fm = FileManager.default
            let from = url(for: old), to = url(for: new)
            guard old != new, fm.fileExists(atPath: from.path) else { return }
            if old.lowercased() == new.lowercased() {
                let temp = dir.appendingPathComponent("history.\(UUID().uuidString).json")
                guard (try? fm.moveItem(at: from, to: temp)) != nil else { return }
                try? fm.moveItem(at: temp, to: to)
                return
            }
            try? fm.removeItem(at: to)
            try? fm.moveItem(at: from, to: to)
        }

        /// Remove Account drops its history, and so does erasing it when Meter history goes Off.
        func delete(_ account: String) {
            HistoryStore.io.sync { _ = try? FileManager.default.removeItem(at: url(for: account)) }
        }
    }

    /// Drops points older than `cutoff`, then keeps at most `maxPoints`. A series is oldest
    /// first, so only its head is read: the usual append parses one or two timestamps. A
    /// timestamp that doesn't parse stops the age trim (the count cap still holds).
    static func trim(_ series: [Point], cutoff: Date) -> [Point] {
        var drop = 0
        for p in series {
            guard let t = date(p.t), t < cutoff else { break }
            drop += 1
        }
        drop = max(drop, series.count - maxPoints)
        return drop > 0 ? Array(series.dropFirst(drop)) : series
    }

    /// The Mac's own timestamps (whole seconds, `Z`) and Windows' (`.ffffff+00:00`). The two
    /// formatters are made once: ISO8601DateFormatter is thread safe, and a chart parses
    /// thousands of points.
    static func date(_ s: String) -> Date? {
        plainFormatter.date(from: s) ?? fractionalFormatter.date(from: s)
    }

    nonisolated(unsafe) private static let plainFormatter = ISO8601DateFormatter()
    nonisolated(unsafe) private static let fractionalFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    /// What the sparklines draw: each tier's last `sparklinePoints` points.
    static func sparklines(_ h: History) -> History {
        h.mapValues { $0.count > sparklinePoints ? Array($0.suffix(sparklinePoints)) : $0 }
    }

    /// The sparklines after a fetch's readings, worked out in memory: the same points the file
    /// gains, so nothing is read back.
    static func sparklines(_ h: History, adding readings: [(Tier, Double)], at now: Date) -> History {
        var out = h
        let iso = ISO8601DateFormatter().string(from: now)
        for (tier, utilization) in readings {
            out[tier.rawValue, default: []].append(.init(t: iso, v: utilization))
        }
        return sparklines(out)
    }

    /// The account's file, once the writes queued before it are done.
    static func load(account: String?) -> History {
        io.sync { Files.standard.load(account) }
    }

    /// One fetch's readings for the account, unless its Meter history is off (`MeterHistory`):
    /// the file is written on `io`, off the main thread. True when they are being recorded.
    @discardableResult
    static func record(_ readings: [(Tier, Double)], account: String?, defaults: DefaultsStore,
                       files: Files = .standard, now: Date = Date()) -> Bool {
        guard !readings.isEmpty, MeterHistory.isOn(account, in: defaults) else { return false }
        io.async { files.append(readings, account: account, now: now) }
        return true
    }
}

/// Settings, Accounts, Meter history (item 43): Off · 30 days per account, 30 days unless chosen.
/// Kept as the labels whose history is off, in the registry's defaults, following renames and
/// removals like the sign-in marker. Labels never leave the defaults: state.yaml shows only the
/// active account's `history_days`.
enum MeterHistory {
    static let offKey = "meterHistoryOff"
    /// The days kept while on (HistoryStore.retention).
    static let days = 30

    /// The label the choice is kept under: the account, or Personal while a launch runs on the
    /// legacy key with no accounts (SignInGate.label).
    static func label(_ account: String?) -> String {
        account ?? AccountRegistry.defaultLabel
    }

    static func offLabels(in store: DefaultsStore) -> [String] {
        store.object(forKey: offKey) as? [String] ?? []
    }

    private static func write(_ labels: [String], in store: DefaultsStore) {
        store.set(labels.isEmpty ? nil : labels, forKey: offKey)
    }

    static func isOn(_ account: String?, in store: DefaultsStore) -> Bool {
        !offLabels(in: store).contains(label(account))
    }

    /// 30 while on, 0 while off (state.yaml `history_days`).
    static func days(_ account: String?, in store: DefaultsStore) -> Int {
        isOn(account, in: store) ? days : 0
    }

    static func set(_ on: Bool, for account: String?, in store: DefaultsStore) {
        let name = label(account)
        var off = offLabels(in: store)
        if on {
            guard off.contains(name) else { return }
            off.removeAll { $0 == name }
        } else {
            guard !off.contains(name) else { return }
            off.append(name)
        }
        write(off, in: store)
    }

    /// The choice follows a renamed account.
    static func rename(_ old: String, to new: String, in store: DefaultsStore) {
        var off = offLabels(in: store)
        guard let i = off.firstIndex(of: old) else { return }
        off[i] = new
        write(off, in: store)
    }

    /// Remove Account forgets it, so a new account with the same label starts at 30 days.
    static func forget(_ account: String, in store: DefaultsStore) {
        var off = offLabels(in: store)
        guard off.contains(account) else { return }
        off.removeAll { $0 == account }
        write(off, in: store)
    }
}
