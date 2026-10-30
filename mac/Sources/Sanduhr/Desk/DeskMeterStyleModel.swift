import Foundation

/// The meter styles' pure rules (2.37.0 item 3), ported from Windows' `DeskMeters` (LitTicks,
/// ShortLabel, the Claude line's Compact form) and the rules of its style renderers
/// (`DeskView.Styles.cs`). Nothing here draws; `DeskMeterStyleViews` reads it.
enum DeskMeterStyleModel {
    /// The cells a `ticks` meter has.
    static let ticks = 10

    /// How many of `cells` tick cells a fill of 0 to 1 lights: to the nearest cell (halves round
    /// up), but any reading above zero lights at least one, so 3% never reads as nothing.
    static func litTicks(_ fill: Double, cells: Int = ticks) -> Int {
        guard fill.isFinite, fill > 0 else { return 0 }
        let n = Int((min(1, fill) * Double(cells)).rounded(.toNearestOrAwayFromZero))
        return min(cells, max(1, n))
    }

    /// The short name a ring carries in its open bottom: "Session" for the five-hour limit, "Week"
    /// for all models, the model for another weekly limit ("Sonnet"), the card's label otherwise.
    static func shortLabel(_ tier: Tier) -> String {
        switch tier {
        case .fiveHour: return "Session"
        case .sevenDay: return "Week"
        default:
            let label = tier.label
            let weekly = "Weekly — "
            return label.hasPrefix(weekly) ? String(label.dropFirst(weekly.count)) : label
        }
    }

    // MARK: What each style shows

    /// The reset line under a meter: only the bar has it; slim, ring and ticks leave it out.
    static func showsReset(_ style: DeskMeterStyle) -> Bool { style == .bar }

    /// A Claude limit's pace mark: every style keeps it.
    static func showsPaceMark(_ style: DeskMeterStyle) -> Bool { true }

    /// The nearly-full look (red, and the glyph beside the percent where there is a percent line)
    /// is kept by every style.
    static func showsWarning(_ style: DeskMeterStyle) -> Bool { true }

    /// Whether the limit's percent sign is printed: a ring prints the bare number inside the arc.
    static func percentText(_ percent: Int, style: DeskMeterStyle) -> String {
        style == .ring ? "\(percent)" : "\(percent)%"
    }

    /// The meters piece in a style: one meter per Claude limit (the bar, slim, ticks) or the arcs
    /// side by side (ring).
    static func metersAreRow(_ style: DeskMeterStyle) -> Bool { style == .ring }

    /// How the `claude` piece draws in a style.
    enum ClaudeForm: Equatable {
        /// Today's one line, "claude   7% session, resets 3:00   63% week".
        case line
        /// The compact two rows, "claude   7% session" over "resets 3:00   63% week" (slim).
        case compactRows
        /// The name over the session and week limits as small gauges in the style (ring, ticks).
        case gauges
    }

    static func claudeForm(_ style: DeskMeterStyle) -> ClaudeForm {
        switch style {
        case .bar: return .line
        case .slim: return .compactRows
        case .ring, .ticks: return .gauges
        }
    }

    // MARK: Gauge readings

    /// One reading a styled meter draws: its label, the short name a ring carries, the percent, the
    /// fill (0 to 1), a Claude limit's pace mark (0 to 1) and whether it is nearly full.
    struct Gauge: Equatable, Identifiable {
        var tier: Tier
        var label: String
        var short: String
        var percent: Int
        var fill: Double
        var pace: Double?
        var warning: Bool
        /// Set by a reading that is not a Claude limit (the system piece's rows), so two share no id.
        var key: String? = nil
        var id: String { key ?? tier.rawValue }
    }

    /// The Claude limits' meters as gauges, in the rows' order.
    static func gauges(_ rows: [DeskMeterRow]) -> [Gauge] {
        rows.map { Gauge(tier: $0.tier, label: $0.label, short: shortLabel($0.tier), percent: $0.percent,
                         fill: $0.fill, pace: $0.pace, warning: $0.warning) }
    }

    /// The `claude` piece's ring and ticks gauges: the session and week limits only, each labelled
    /// "session" or "week" (Windows' lowercase names), the rest of the reading as the meters have it.
    /// Empty while a sign-in is needed, so the piece falls back to its two rows.
    static func claudeGauges(_ rows: [DeskMeterRow], signInNeeded: Bool) -> [Gauge] {
        guard !signInNeeded else { return [] }
        return rows.filter { $0.tier == .fiveHour || $0.tier == .sevenDay }.map { r in
            let name = r.tier == .fiveHour ? "session" : "week"
            return Gauge(tier: r.tier, label: name, short: name, percent: r.percent, fill: r.fill,
                         pace: r.pace, warning: r.warning)
        }
    }

    // MARK: Geometry

    /// Where a ring's arc starts (degrees clockwise from three o'clock: lower left) and how far it
    /// sweeps, leaving the bottom open.
    static let ringStart = 135.0
    static let ringSweep = 270.0

    /// The ring's arc for a fill of 0 to 1 as the fraction of a full circle `Circle.trim` takes;
    /// 0 for nothing (no round-cap dot is drawn for it).
    static func ringTrim(_ fill: Double) -> Double {
        let f = min(1, max(0, fill.isFinite ? fill : 0))
        return f <= 0.001 ? 0 : f * ringSweep / 360
    }

    /// The angle in degrees (clockwise from three o'clock) where a ring's pace mark crosses the arc.
    static func ringPaceAngle(_ pace: Double) -> Double { ringStart + ringSweep * min(1, max(0, pace)) }

    /// Where the 2-point pace mark of a slim or ticks meter starts along a `width` meter.
    static func markOffset(_ pace: Double, width: Double) -> Double {
        max(0, min(width - 2, pace * width - 1))
    }

    /// A slim meter's filled width.
    static func slimFillWidth(_ fill: Double, width: Double) -> Double {
        (width * min(1, max(0, fill.isFinite ? fill : 0))).rounded()
    }
}

/// The `claude` piece's compact two-row form (Windows' `DeskClaudeText.Compact`), so it no longer
/// runs wide on a narrow or slanted stack.
struct DeskClaudeCompact: Equatable {
    /// The account label with two or more accounts, nil with one (the row then starts with "claude").
    var account: String?
    /// The first row's text after the name ("7% session"), nil when only the name fits there.
    var topRest: String?
    /// The second row ("resets 3:00   63% week", or the sign-in line), nil when there is none.
    var bottom: String?

    var name: String { account ?? "claude" }

    /// The rows as plain text: "claude   7% session" over "resets 3:00   63% week".
    var lines: [String] {
        let top = topRest.map { "\(name)   \($0)" } ?? name
        return bottom.map { [top, $0] } ?? [top]
    }

    /// Nil with nothing to say; the sign-in line goes under the name (nil with the reminder off).
    /// `zone` converts the session's reset time (default: local), for deterministic tests.
    static func make(_ input: DeskUsage, zone: TimeZone? = nil) -> DeskClaudeCompact? {
        if input.signInNeeded {
            return input.signInReminder
                ? DeskClaudeCompact(account: input.account, topRest: nil, bottom: DeskClaudeText.signInLine(first: input.firstSignIn))
                : nil
        }
        guard let tiers = input.usage?.tiers else { return nil }
        let session = tiers[.fiveHour]?.utilization.map { (value: $0, reset: tiers[.fiveHour]?.resetsAt) }
        let week = tiers[.sevenDay]?.utilization
        if session == nil, week == nil { return nil }
        var bottom: [String] = []
        if let s = session, let reset = parseISO(s.reset) {
            let f = DateFormatter()
            f.dateFormat = "h:mm"
            if let zone { f.timeZone = zone }
            bottom.append("resets \(f.string(from: reset))")
        }
        if let w = week { bottom.append("\(Int(w))% week") }
        return DeskClaudeCompact(account: input.account,
                                 topRest: session.map { "\(Int($0.value))% session" },
                                 bottom: bottom.isEmpty ? nil : bottom.joined(separator: "   "))
    }
}
