import Foundation

/// What Desk knows about Claude usage, handed over by the widget's view model after every
/// refresh. Desk reads it in process instead of polling snapshot.json, which the widget still
/// writes for the statusline and the MCP server.
struct DeskUsage {
    /// The last successful fetch, kept through failed ones.
    var usage: UsageResponse?
    /// When `usage` was fetched; nil before the first success.
    var fetchedAt: Date?
    /// The session key or Cloudflare clearance was refused, or there is none yet, so only a
    /// (new) sign-in helps.
    var signInNeeded = false
    /// There has never been a key (a fresh install): the line asks to sign in, not to sign in again.
    var firstSignIn = false
    /// The active account as the claude line names it, with two or more accounts ("Work", or
    /// "Work (in use)" just after an automatic switch); nil with one. The notch never shows it.
    var account: String?
    /// An account switch is under way: `usage` is the old account's, laid out but drawn unseen
    /// (AccountSwitchFade), so the notch drops it and the meters and the line keep their place.
    var veiled = false
    /// The switch's fetch outlasts the fade: the faint "switching account…".
    var switchNote = false

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
    /// Nearly full with the reset still far away (MeterWarning): the bar draws red with an ink glow.
    var warning = false

    var id: String { tier.rawValue }

    /// One row per tier the server reported with a utilization, in the widget's display order.
    /// `warnings` gives each tier's warning setting; the default is the built-in one, so only the
    /// app hands over the saved settings (`MeterWarningSettings.saved`).
    static func rows(from usage: UsageResponse?, now: Date = Date(),
                     warnings: (Tier) -> MeterWarningSettings = MeterWarningSettings.standard(for:)) -> [DeskMeterRow] {
        guard let usage else { return [] }
        return Tier.allCases.compactMap { tier in
            guard let t = usage.tiers[tier], let util = t.utilization else { return nil }
            return DeskMeterRow(tier: tier,
                                label: tier.label,
                                percent: Int(util),
                                fill: min(1, max(0, util / 100)),
                                pace: paceFrac(t.resetsAt, tier: tier, now: now),
                                reset: resetDateTimeStr(t.resetsAt, now: now),
                                warning: MeterWarning.isWarning(t, settings: warnings(tier), now: now))
        }
    }
}

/// The text forms of the same numbers: the one-line `claude` piece and the notch's short meters.
enum DeskClaudeText {
    /// "claude   7% session, resets 3:00   63% week", or the sign-in line, or nil with no numbers.
    /// With two or more accounts the line starts with the active label in place of "claude"
    /// ("Work   7% session …").
    static func line(_ input: DeskUsage) -> String? {
        parts(input).map { "\($0.account ?? "claude")   \($0.rest)" }
    }

    /// The line in its two pieces: the account label (nil with one account, where the line starts
    /// with "claude") and everything after it. The Desk draws the label as its own element, the
    /// one that switches accounts when clicked.
    struct Parts: Equatable {
        var account: String?
        var rest: String
    }

    /// The Desk's sign-in line: "sign in to Sanduhr" before the first key, else "sign in again in Sanduhr".
    static func signInLine(first: Bool) -> String { first ? compactSignIn : "sign in again in Sanduhr" }

    static func parts(_ input: DeskUsage) -> Parts? {
        if input.signInNeeded { return Parts(account: input.account, rest: signInLine(first: input.firstSignIn)) }
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
        return parts.isEmpty ? nil : Parts(account: input.account, rest: parts.joined(separator: "   "))
    }

    /// The notch's short sign-in line, for the wings and the strip.
    static let compactSignIn = "sign in to Sanduhr"

    /// "5h 7%  wk 63%" for the notch, the short sign-in line when only a new sign-in helps,
    /// nil when stale, empty or veiled by a switch (the old account's numbers).
    static func compact(_ input: DeskUsage, now: Date = Date()) -> String? {
        if input.signInNeeded { return compactSignIn }
        guard !input.veiled, !input.isStale(now: now), let tiers = input.usage?.tiers else { return nil }
        let text = [tiers[.fiveHour]?.utilization.map { "5h \(Int($0))%" },
                    tiers[.sevenDay]?.utilization.map { "wk \(Int($0))%" }]
            .compactMap { $0 }.joined(separator: "  ")
        return text.isEmpty ? nil : text
    }
}
