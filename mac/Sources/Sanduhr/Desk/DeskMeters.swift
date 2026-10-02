import Foundation

/// What Desk knows about Claude usage, handed over by the widget's view model after every
/// refresh. Desk reads it in process instead of polling snapshot.json, which the widget still
/// writes for the statusline and the MCP server.
struct DeskUsage {
    /// The last successful fetch, kept through failed ones.
    var usage: UsageResponse?
    /// When `usage` was fetched; nil before the first success.
    var fetchedAt: Date?
    /// The session key or Cloudflare clearance was refused, so only a new sign-in helps.
    var signInNeeded = false

    /// Numbers older than this are drawn dimmed, and the notch drops them.
    static let staleAfter: TimeInterval = 15 * 60

    func isStale(now: Date) -> Bool {
        signInNeeded || fetchedAt.map { now.timeIntervalSince($0) > Self.staleAfter } ?? true
    }
}

/// One limit as the Desk meters draw it: label, bar fill, pace tick, percent and reset time.
/// Built from the same helpers the widget's tier cards use, so the two always agree.
struct DeskMeterRow: Identifiable, Equatable {
    let tier: Tier
    /// The widget's label for the tier ("Session (5hr)").
    let label: String
    /// Utilization as the widget prints it; can pass 100.
    let percent: Int
    /// How much of the bar to fill, 0 to 1. Clamped, so a limit over 100% fills the bar and stops.
    let fill: Double
    /// Where the pace tick goes, 0 to 1, or nil without a reset time.
    let pace: Double?
    /// "Today 5:00 PM", or empty without a reset time.
    let reset: String

    var id: String { tier.rawValue }

    /// One row per tier the server reported with a utilization, in the widget's display order.
    static func rows(from usage: UsageResponse?, now: Date = Date()) -> [DeskMeterRow] {
        guard let usage else { return [] }
        return Tier.allCases.compactMap { tier in
            guard let t = usage.tiers[tier], let util = t.utilization else { return nil }
            return DeskMeterRow(tier: tier,
                                label: tier.label,
                                percent: Int(util),
                                fill: min(1, max(0, util / 100)),
                                pace: paceFrac(t.resetsAt, tier: tier, now: now),
                                reset: resetDateTimeStr(t.resetsAt, now: now))
        }
    }
}

/// The text forms of the same numbers: the one-line `claude` piece and the notch's short meters.
enum DeskClaudeText {
    /// "claude   7% session, resets 3:00   63% week", or the sign-in line, or nil with no numbers.
    static func line(_ input: DeskUsage) -> String? {
        if input.signInNeeded { return "claude   sign in again in Sanduhr" }
        guard let tiers = input.usage?.tiers else { return nil }
        var parts: [String] = []
        if let s = tiers[.fiveHour], let util = s.utilization {
            var part = "\(Int(util))% session"
            if let reset = parseISO(s.resetsAt) {
                let f = DateFormatter()
                f.dateFormat = "h:mm"
                part += ", resets \(f.string(from: reset))"
            }
            parts.append(part)
        }
        if let w = tiers[.sevenDay]?.utilization { parts.append("\(Int(w))% week") }
        return parts.isEmpty ? nil : "claude   " + parts.joined(separator: "   ")
    }

    /// "5h 7%  wk 63%" for the notch, nil when stale or empty.
    static func compact(_ input: DeskUsage, now: Date = Date()) -> String? {
        guard !input.isStale(now: now), let tiers = input.usage?.tiers else { return nil }
        let text = [tiers[.fiveHour]?.utilization.map { "5h \(Int($0))%" },
                    tiers[.sevenDay]?.utilization.map { "wk \(Int($0))%" }]
            .compactMap { $0 }.joined(separator: "  ")
        return text.isEmpty ? nil : text
    }
}
