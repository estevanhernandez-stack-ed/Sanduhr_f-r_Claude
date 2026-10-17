import Foundation

/// The one ISO 8601 spelling behind every stamp the app writes and reads: the handoff request
/// and result files, snapshot.json, the meter history, the usage API's `resets_at`. Internet
/// date-time in UTC, with milliseconds (`2026-10-04T17:03:45.250Z`) or whole seconds
/// (`2026-10-04T17:03:45Z`); each file keeps the form it has always carried. The Windows build
/// writes the same files with .NET's seven fraction digits and the usage API answers with
/// numeric offsets, so the parser takes any fraction length and `Z` or `+02:00` alike.
enum ISO8601 {
    /// `date` as an internet date-time in UTC: milliseconds by default, whole seconds (cut, not
    /// rounded) with `fractional: false`.
    static func stamp(_ date: Date, fractional: Bool = true) -> String {
        lock.withLock { (fractional ? fractionalFormatter : plainFormatter).string(from: date) }
    }

    /// The instant `string` names, or nil. Whitespace around it is ignored, and a fraction longer
    /// than milliseconds is cut to three digits before the formatter sees it.
    static func parse(_ string: String) -> Date? {
        var s = string.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return nil }
        if let r = s.range(of: #"(\.\d{3})\d+"#, options: .regularExpression) {
            s.replaceSubrange(r, with: String(s[r].prefix(4)))
        }
        return lock.withLock { fractionalFormatter.date(from: s) ?? plainFormatter.date(from: s) }
    }

    /// Made once and shared under the lock: ISO8601DateFormatter is not documented thread safe,
    /// the callers run off the main actor, and a chart parses thousands of points.
    private static let lock = NSLock()
    nonisolated(unsafe) private static let fractionalFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
    nonisolated(unsafe) private static let plainFormatter = ISO8601DateFormatter()
}
