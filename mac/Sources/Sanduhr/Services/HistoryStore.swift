import Foundation

/// Per-tier rolling history used to draw sparklines.
/// File format (JSON) matches the Python version's `history.json` so both
/// implementations could theoretically share data:
///     { "five_hour": [ { "t": "2026-04-15T...", "v": 42 }, ... ], ... }
/// sanduhr.py:113-130.
enum HistoryStore {
    /// 24 points × 5-min refresh = 2-hour window. sanduhr.py:41.
    static let maxPoints = 24

    struct Point: Codable, Equatable {
        let t: String   // ISO-8601 timestamp
        let v: Double   // utilization 0–100
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

        /// Append a reading and trim to `maxPoints`. Returns the updated series.
        @discardableResult
        func append(_ tier: Tier, utilization: Double, account: String?, now: Date = Date()) -> [Point] {
            var h = load(account)
            let iso = ISO8601DateFormatter().string(from: now)
            var series = h[tier.rawValue] ?? []
            series.append(.init(t: iso, v: utilization))
            if series.count > maxPoints {
                series.removeFirst(series.count - maxPoints)
            }
            h[tier.rawValue] = series
            save(h, account: account)
            return series
        }

        /// The upgrade's one-time rename of `history.json` to `history.{label}.json`, never over
        /// an existing file. True when it moved.
        @discardableResult
        func adoptLegacy(into label: String) -> Bool {
            let fm = FileManager.default
            let legacy = url(for: nil), target = url(for: label)
            guard fm.fileExists(atPath: legacy.path), !fm.fileExists(atPath: target.path) else { return false }
            return (try? fm.moveItem(at: legacy, to: target)) != nil
        }

        /// The file follows a renamed account. A change of case alone goes through a temporary
        /// name, since the Mac's file system sees both names as one file.
        func rename(_ old: String, to new: String) {
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

        /// Remove Account drops its history.
        func delete(_ account: String) {
            try? FileManager.default.removeItem(at: url(for: account))
        }
    }

    static func load(account: String?) -> History {
        Files.standard.load(account)
    }

    @discardableResult
    static func append(_ tier: Tier, utilization: Double, account: String?) -> [Point] {
        Files.standard.append(tier, utilization: utilization, account: account)
    }

    static func series(for tier: Tier, account: String?) -> [Double] {
        (load(account: account)[tier.rawValue] ?? []).map(\.v)
    }
}
