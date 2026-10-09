import Foundation

/// Settings, Claude Usage, Meters (item 37): the meter history (HistoryStore) as a chart per limit,
/// for one account or every account overlaid, and the same readings as CSV. Windows 2.2 parity:
/// its History tab's Week/Month window, "All accounts" overlay with one color per account in
/// registry order, and `CsvExport` columns, ordering and quoting.
enum MeterHistoryChart {
    /// Week or Month, as Windows names them.
    enum Window: String, CaseIterable, Identifiable, Sendable {
        case week, month

        var id: String { rawValue }
        var title: String { self == .week ? "Week" : "Month" }
        var days: Int { self == .week ? 7 : 30 }
    }

    /// One account's line on one limit's chart, oldest first.
    struct Series: Equatable, Sendable {
        let account: String
        let points: [Reading]
    }

    struct Reading: Equatable, Sendable {
        let date: Date
        let value: Double
    }

    /// One limit's chart: its series (one, or one per account with readings), never empty.
    struct Row: Equatable, Sendable, Identifiable {
        let tier: String
        let title: String
        let series: [Series]
        var id: String { tier }
    }

    /// The charts for `accounts` (one for a single account, the registry's order for all) over
    /// the window ending `now`. Limits follow Tier's order, then any other key the file holds
    /// (a newer build's or Windows'), sorted; a limit with no reading in the window is dropped.
    /// `hidden` (the limits switched off in Settings, Alerts, Meters) are left out.
    static func rows(histories: [String: HistoryStore.History], accounts: [String], window: Window,
                     now: Date, hidden: Set<String> = []) -> [Row] {
        let cutoff = now.addingTimeInterval(-TimeInterval(window.days) * 86_400)
        let keys = Set(accounts.flatMap { histories[$0]?.keys.map { $0 } ?? [] }).subtracting(hidden)
        return order(keys).compactMap { key in
            let series = accounts.compactMap { account -> Series? in
                // Oldest first: walk back from the newest and stop at the window's start, so a
                // week parses about a week of points. An unparsable timestamp is skipped.
                var points: [Reading] = []
                for p in (histories[account]?[key] ?? []).reversed() {
                    guard let d = HistoryStore.date(p.t) else { continue }
                    if d < cutoff { break }
                    if d <= now { points.append(Reading(date: d, value: min(100, max(0, p.v)))) }
                }
                return points.isEmpty ? nil : Series(account: account, points: points.reversed())
            }
            return series.isEmpty ? nil : Row(tier: key, title: title(key), series: series)
        }
    }

    /// Tier's order first, then unknown keys alphabetically.
    static func order(_ keys: Set<String>) -> [String] {
        let known = Tier.allCases.map(\.rawValue).filter(keys.contains)
        return known + keys.subtracting(known).sorted()
    }

    /// Tier's label, or the raw key for one this build doesn't know.
    static func title(_ key: String) -> String {
        Tier(rawValue: key)?.label ?? key
    }

    // MARK: Colors

    /// The overlay's colors, one per account in registry order, repeating after six.
    static let palette = ["60a5fa", "f472b6", "34d399", "fbbf24", "a78bfa", "fb923c"]

    /// An account's color: its place in the registry, so it stays put whatever is shown.
    static func color(for account: String, in accounts: [String]) -> String {
        let i = accounts.firstIndex(of: account) ?? 0
        return palette[i % palette.count]
    }

    // MARK: CSV

    /// Windows' `CsvExport.Build`: with `account` nil, every account in `accounts` with an
    /// `account` column (`timestamp,account,tier,util_pct`); else that account alone
    /// (`timestamp,tier,util_pct`). Every reading in the file, not only the chart's window. Rows
    /// sort by the timestamp text (stable), CRLF line ends, a field quoted only when it holds a
    /// comma, quote, CR or LF. The header is written even with no rows.
    static func csv(histories: [String: HistoryStore.History], accounts: [String], account: String?) -> (text: String, rowCount: Int) {
        var rows: [[String]] = []
        let header: [String]
        if let account {
            header = ["timestamp", "tier", "util_pct"]
            for (tier, points) in (histories[account] ?? [:]).sorted(by: { $0.key < $1.key }) {
                rows += points.map { [$0.t, tier, number($0.v)] }
            }
        } else {
            header = ["timestamp", "account", "tier", "util_pct"]
            for label in accounts {
                for (tier, points) in (histories[label] ?? [:]).sorted(by: { $0.key < $1.key }) {
                    rows += points.map { [$0.t, label, tier, number($0.v)] }
                }
            }
        }
        let ordered = rows.enumerated().sorted {
            $0.element[0] == $1.element[0] ? $0.offset < $1.offset : $0.element[0] < $1.element[0]
        }.map(\.element)
        let text = ([header] + ordered).map { $0.map(escape).joined(separator: ",") + "\r\n" }.joined()
        return (text, ordered.count)
    }

    /// RFC 4180 minimal quoting.
    static func escape(_ field: String) -> String {
        guard field.unicodeScalars.contains(where: { ",\"\r\n".unicodeScalars.contains($0) }) else { return field }
        return "\"" + field.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }

    /// 42 as "42", 42.5 as "42.5": .NET's invariant formatting of a double.
    static func number(_ v: Double) -> String {
        if v.isFinite, v == v.rounded(), abs(v) < 1e15 { return String(Int64(v)) }
        return String(v)
    }

    /// `Sanduhr-usage-all-accounts-2026-10-10.csv`, or the account's label lowercased with
    /// dashes for spaces, as Windows names it.
    static func exportName(account: String?, day: Date, calendar: Calendar = .current) -> String {
        let scope = account.map { $0.lowercased().replacingOccurrences(of: " ", with: "-") } ?? "all-accounts"
        let c = calendar.dateComponents([.year, .month, .day], from: day)
        return String(format: "Sanduhr-usage-%@-%04d-%02d-%02d.csv", scope, c.year ?? 0, c.month ?? 0, c.day ?? 0)
    }

    /// The latest reading of each series, for the row's caption and VoiceOver.
    static func latest(_ s: Series) -> Double? { s.points.last?.value }
}
