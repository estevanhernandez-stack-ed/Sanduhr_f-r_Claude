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

    private static let fileURL: URL = {
        let fm = FileManager.default
        let base = (try? fm.url(for: .applicationSupportDirectory,
                                in: .userDomainMask,
                                appropriateFor: nil, create: true))
            ?? fm.homeDirectoryForCurrentUser
                .appendingPathComponent("Library/Application Support")
        let dir = base.appendingPathComponent("Sanduhr", isDirectory: true)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("history.json")
    }()

    static func load() -> History {
        guard let data = try? Data(contentsOf: fileURL),
              let h = try? JSONDecoder().decode(History.self, from: data)
        else { return [:] }
        return h
    }

    static func save(_ h: History) {
        guard let data = try? JSONEncoder().encode(h) else { return }
        try? data.write(to: fileURL, options: .atomic)
    }

    /// Append a reading and trim to `maxPoints`. Returns the updated series.
    @discardableResult
    static func append(_ tier: Tier, utilization: Double) -> [Point] {
        var h = load()
        let iso = ISO8601DateFormatter().string(from: Date())
        var series = h[tier.rawValue] ?? []
        series.append(.init(t: iso, v: utilization))
        if series.count > maxPoints {
            series.removeFirst(series.count - maxPoints)
        }
        h[tier.rawValue] = series
        save(h)
        return series
    }

    static func series(for tier: Tier) -> [Double] {
        (load()[tier.rawValue] ?? []).map(\.v)
    }
}
