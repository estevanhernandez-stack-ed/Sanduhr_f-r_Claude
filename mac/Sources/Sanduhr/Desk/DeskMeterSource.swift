import Foundation

/// Which source a usage piece draws (agent sources design, section 2, "Desk meter source"; Windows'
/// `DeskSources`): `combined` (every source that is on, the default and what every layout written before
/// 2.46 meant), `claude` or `codex`. The layout word carries it as a `src=` field, written only when not
/// combined.
enum DeskMeterSource: String, CaseIterable, Hashable {
    case combined, claude, codex
}

/// Words, names and the pieces that take a `DeskMeterSource`.
enum DeskMeterSources {
    /// The pieces that take a source: the Claude line and the Claude limits (Windows' list too). The
    /// system piece has a style but no usage, so it has none.
    static let pieces = ["claude", "meters"]

    /// The layout field's prefix: `src=codex`.
    static let prefix = "src="

    static func supports(_ widget: String) -> Bool { pieces.contains(widget) }

    /// The source for an exact lowercase word, nil for anything else.
    static func parse(_ word: String?) -> DeskMeterSource? { word.flatMap(DeskMeterSource.init(rawValue:)) }

    /// The source a piece can carry: `source` for a piece that takes one, `.combined` for any other.
    static func `for`(_ widget: String, _ source: DeskMeterSource) -> DeskMeterSource {
        supports(widget) ? source : .combined
    }

    /// The layout field's text after the prefix, as a source: an unknown word is combined.
    static func fromField(_ widget: String, _ text: String) -> DeskMeterSource {
        `for`(widget, parse(text) ?? .combined)
    }

    /// As Settings, Desk names it (Windows' words).
    static func name(_ s: DeskMeterSource) -> String {
        switch s {
        case .combined: return "Combined"
        case .claude: return "Claude"
        case .codex: return "Codex"
        }
    }

    /// The Shows picker's label.
    static let title = "Shows"

    static let caption = "Combined draws every source that is on in one bar; Claude or Codex draws that source alone."

    /// Why a design or layout may not give `widget` the source word `word`, nil when it may.
    static func refusal(widget: String, word: String?) -> String? {
        guard supports(widget) else { return "only " + pieces.joined(separator: ", ") + " take a source" }
        guard parse(word?.trimmingCharacters(in: .whitespaces).lowercased()) != nil else {
            return "must be one of " + DeskMeterSource.allCases.map(\.rawValue).joined(separator: ", ")
        }
        return nil
    }

    /// Whether Settings and Arrange offer Shows under `widget`: the piece takes a source and Codex is on
    /// (with Claude alone there is nothing to choose between).
    static func offers(_ widget: String, codexOn: Bool) -> Bool { codexOn && supports(widget) }
}

/// Whether Codex is on, for the places that offer Shows (Settings, Arrange): the setup's switch.
enum DeskMeterSourceSettings {
    static func codexOn(setup: SetupSettings? = SetupStore.load(), sources: AgentSourceSettings = .load()) -> Bool {
        sources.isOn(.codex, setup: setup)
    }
}

/// What the Desk is told about sources besides Claude: whether Codex is on, what it read, Primary and
/// each source's color. `claudeOnly` (a Mac with only Claude on) draws every piece exactly as 2.45.0.
struct DeskSourceInput: Equatable {
    var codexOn = false
    var codex: CodexWidgetReading = .none
    var primary: AgentSource = .claude
    /// Resolved `#RRGGBB` per source; a missing source reads as its default.
    var colors: [AgentSource: String] = [:]

    static let claudeOnly = DeskSourceInput()

    func color(of source: AgentSource) -> String { colors[source] ?? source.defaultColor }

    /// What the Desk is told: Codex on from the setup, its reading (demo mode's sample numbers in demo, as
    /// the widget shows them), Primary and the source colors. Codex off is `claudeOnly`, whatever else is set,
    /// so a Mac with only Claude on never carries any of it.
    static func make(setup: SetupSettings?, sources: AgentSourceSettings, codex: CodexWidgetReading,
                     demo: Bool, now: Date) -> DeskSourceInput {
        guard sources.isOn(.codex, setup: setup) else { return .claudeOnly }
        return DeskSourceInput(codexOn: true, codex: demo ? WidgetSources.demoReading(now: now) : codex,
                               primary: sources.primary(setup: setup),
                               colors: [.claude: sources.color(of: .claude), .codex: sources.color(of: .codex)])
    }

    /// Codex is on and has a five-hour or weekly limit to draw.
    var hasCodexLimits: Bool {
        guard codexOn, let usage = codex.usage else { return false }
        return DeskSourceRows.windows.contains { usage.tiers[$0.tier]?.utilization != nil }
    }
}

/// What one source's piece draws, built pure (the Desk model stores it, the views read it).
struct DeskSourceView: Equatable {
    /// The meters piece's rows.
    var rows: [DeskMeterRow] = []
    /// The claude piece's ring and ticks gauges (session and week).
    var gauges: [DeskMeterStyleModel.Gauge] = []
    /// A Codex piece's bar-style line ("Codex 5h 40% · wk 12%"); nil with none, and for a combined piece, whose line is Claude's.
    var line: String?
    /// A Codex piece's slim form ("Codex   30% session" over "resets 5:00   70% week"); nil with none.
    var compact: DeskClaudeCompact?
    /// The name the claude piece's gauges carry: "Codex" for a Codex piece, nil otherwise (the account or "claude").
    var name: String?
    /// The whole block dims: every Codex window is stale.
    var stale = false
}

enum DeskSourceRows {
    /// The windows a joined row pairs, and the tier each source reports it under (the widget's table).
    static var windows: [(window: SourceBarWindow, tier: Tier)] { WidgetSources.windows }

    private static func window(of tier: Tier) -> SourceBarWindow? { windows.first { $0.tier == tier }?.window }

    // MARK: Which source a piece draws

    /// What a piece set to `source` draws. Codex off is today's piece whatever the word says (the
    /// picker is hidden then, and turning Codex off must not blank the Desk). Codex on: a Codex piece
    /// is Codex; a combined piece is the joined rows when Claude and Codex both have numbers, Codex
    /// alone when there is no Claude to draw (a Codex-only setup), else Claude as today.
    /// `claudePresent`: Claude has numbers or a sign-in line. `claudeNumbers`: it has numbers.
    static func resolve(_ source: DeskMeterSource, input: DeskSourceInput, claudePresent: Bool, claudeNumbers: Bool) -> DeskMeterSource {
        guard input.codexOn else { return .claude }
        switch source {
        case .claude: return .claude
        case .codex: return .codex
        case .combined:
            if !claudePresent && input.hasCodexLimits { return .codex }
            return claudeNumbers && input.hasCodexLimits ? .combined : .claude
        }
    }

    // MARK: Codex alone

    /// "Codex session (5hr)" and "Codex weekly": the row label on a Codex-only piece, so it can't be taken for Claude's.
    static func codexRowLabel(_ window: SourceBarWindow) -> String {
        let label = window.label
        return "Codex " + label.prefix(1).lowercased() + label.dropFirst()
    }

    /// One row per Codex window with a number. No pace mark (the pace is Claude's) and no warning (a Codex
    /// alert pulses its row, `DeskPulses`, but a warning is Claude's); each row is stale on its own.
    static func codexRows(_ input: DeskSourceInput, now: Date) -> [DeskMeterRow] {
        guard input.codexOn, let usage = input.codex.usage else { return [] }
        return windows.compactMap { w in
            guard let t = usage.tiers[w.tier], let util = t.utilization else { return nil }
            return row(w.tier, t, util, label: codexRowLabel(w.window), pace: nil, now: now,
                       stale: !input.codex.freshTiers.contains(w.tier))
        }
    }

    /// "Codex 5h 40% · wk 12%", with only the windows Codex reports; nil with none.
    static func codexLine(_ input: DeskSourceInput) -> String? {
        guard input.codexOn else { return nil }
        let text = WidgetSources.summary(input.codex.usage)
        return text.isEmpty ? nil : "Codex " + text
    }

    /// Every Codex window is stale (none is fresh): the Codex line and block dim.
    static func codexStale(_ input: DeskSourceInput) -> Bool {
        input.codex.freshTiers.isEmpty
    }

    private static func row(_ tier: Tier, _ t: TierUsage, _ util: Double, label: String, pace: Double?, now: Date,
                            stale: Bool, layout: SourceBarLayout? = nil) -> DeskMeterRow {
        DeskMeterRow(tier: tier, label: label, percent: Int(util), fill: min(1, max(0, util / 100)), pace: pace,
                     reset: resetDateTimeStr(t.resetsAt, now: now), warning: false, layout: layout, stale: stale)
    }

    // MARK: Combined

    /// `claude` (Claude's rows, in order) with each five-hour and weekly row that Codex also reports joined:
    /// the window's label, the number and reset of the primary source, the pace mark only when Claude is
    /// primary, and the `SourceBar` layout of both. A joined row carries no warning (D10: warnings stay on
    /// each source's own card). Any other row (a model's weekly) stays as it is.
    static func combinedRows(claude: [DeskMeterRow], claudeUsage: UsageResponse?, input: DeskSourceInput,
                             now: Date) -> [DeskMeterRow] {
        claude.map { joined($0, claudeUsage: claudeUsage, input: input, now: now) ?? $0 }
    }

    private static func joined(_ row: DeskMeterRow, claudeUsage: UsageResponse?, input: DeskSourceInput,
                               now: Date) -> DeskMeterRow? {
        guard let window = window(of: row.tier), let claudeTier = claudeUsage?.tiers[row.tier],
              let claudePercent = claudeTier.utilization,
              let codexTier = input.codex.usage?.tiers[row.tier], let codexPercent = codexTier.utilization else { return nil }
        let when = input.codex.asOf.map { WidgetSources.asOfWhen($0, now: now) }
        let codexStale = !input.codex.freshTiers.contains(row.tier)
        let points = [SourceBarPoint(source: .claude, percent: claudePercent, colorHex: input.color(of: .claude)),
                      SourceBarPoint(source: .codex, percent: codexPercent, stale: codexStale,
                                     colorHex: input.color(of: .codex), asOf: when)]
        let layout = SourceBar.layout(primary: input.primary, window: window, points: points)
        let codexLeads = layout.primary?.source == .codex
        if codexLeads {
            return self.row(row.tier, codexTier, codexPercent, label: window.label, pace: nil, now: now,
                            stale: codexStale, layout: layout)
        }
        var out = row
        out.label = window.label
        out.warning = false
        out.layout = layout
        return out
    }

    // MARK: Views

    /// The Codex piece's view: its rows, the session and week gauges for the claude piece, the line.
    static func codexView(_ input: DeskSourceInput, now: Date) -> DeskSourceView {
        let rows = codexRows(input, now: now)
        let stale = codexStale(input)
        let numbers = DeskUsage(usage: input.codexOn ? input.codex.usage : nil, fetchedAt: stale ? nil : now, account: "Codex")
        return DeskSourceView(rows: rows, gauges: DeskMeterStyleModel.claudeGauges(rows, signInNeeded: false),
                              line: codexLine(input), compact: DeskClaudeCompact.make(numbers), name: "Codex", stale: stale)
    }

    /// The combined piece's view: Claude's rows with the joined ones carrying their layouts. `meters` are the
    /// rows the meters piece shows (hidden limits left out); `all` every reported limit, for the claude
    /// piece's session and week gauges (the line reads them whatever the cards show).
    static func combinedView(meters: [DeskMeterRow], all: [DeskMeterRow], claudeUsage: UsageResponse?,
                             input: DeskSourceInput, now: Date) -> DeskSourceView {
        DeskSourceView(
            rows: combinedRows(claude: meters, claudeUsage: claudeUsage, input: input, now: now),
            gauges: DeskMeterStyleModel.claudeGauges(
                combinedRows(claude: all, claudeUsage: claudeUsage, input: input, now: now), signInNeeded: false))
    }
}
