import Foundation
import Observation

/// The widget with two sources (agent layer spec, section 3; agent sources design, section 2): what the
/// card stack draws, built pure so tests assert on the rows and never on rendering. With only Claude on
/// it is today's widget: one headerless Claude section whose rows are `UsageViewModel.visibleTiers`.
/// With Claude and Codex on: a combined bar per window both report (5-hour, weekly), then a section
/// per source, each under a header (name, color, collapse chevron). Windows' `WidgetViewModel`
/// (`RebuildSections`, `RenderCodex`, `RebuildCombined`) is the parity reference.

/// What Codex's service has published, as the widget reads it.
struct CodexWidgetReading: Equatable {
    var usage: UsageResponse?
    var asOf: Date?
    var freshTiers: Set<Tier> = []
    var emptyState: CodexUsageService.EmptyState?

    static let none = CodexWidgetReading()
}

struct WidgetSourcesInput {
    var claudeOn: Bool
    /// Claude's shown numbers (`vm.shownUsage`).
    var claudeUsage: UsageResponse?
    /// Claude's hidden limits (`vm.hiddenTiers`).
    var hidden: Set<Tier> = []
    var codexOn: Bool
    var codex: CodexWidgetReading = .none
    var primary: AgentSource = .claude
    var settings: AgentSourceSettings = .default
    var collapsed: Set<AgentSource> = []
    var compact = false
    /// Demo mode: Codex shows `DemoMode.codexUsage`, fresh, as of now, whatever the service holds.
    var demo = false
    var now = Date()
    var calendar = Calendar.current
    var locale = Locale.current
}

/// One card of a source's section. Claude's keep their own labels; Codex's say the window.
struct WidgetSourceCard: Equatable {
    var tier: Tier
    var usage: TierUsage
    /// The number can't be trusted now: dimmed, no pace or burn.
    var stale = false
    var label: String
    var spokenLabel: String
}

struct WidgetSourceHeader: Equatable {
    var source: AgentSource
    var name: String
    var colorHex: String
    var collapsed: Bool
    /// "as of 3:42 PM" for a source with a reading time (Codex); nil otherwise.
    var asOf: String?
    /// The whole reading is stale: the "as of" reads dim.
    var stale = false
    /// "5h 40% · wk 12%", shown while collapsed.
    var summary: String
}

enum WidgetSourceContent: Equatable {
    case cards([WidgetSourceCard])
    /// The line shown instead of cards ("Run Codex once to see your limits.").
    case empty(String)
    case collapsed
}

struct WidgetSourceSection: Equatable {
    var source: AgentSource
    /// Nil for a lone Claude section (today's widget) and in compact.
    var header: WidgetSourceHeader?
    var content: WidgetSourceContent
}

struct WidgetCombinedBar: Equatable {
    var window: SourceBarWindow
    var layout: SourceBarLayout
}

struct WidgetSourceLayout: Equatable {
    var combined: [WidgetCombinedBar] = []
    var sections: [WidgetSourceSection] = []

    /// Only Claude's section, without a header: the widget draws exactly as it did before sources.
    var isClaudeOnly: Bool { combined.isEmpty && sections.count == 1 && sections[0].source == .claude && sections[0].header == nil }

    /// Claude's cards as the card stack takes them; empty when Claude's section is absent or collapsed.
    var claudeRows: [(tier: Tier, usage: TierUsage)] {
        guard let s = sections.first(where: { $0.source == .claude }), case .cards(let cards) = s.content else { return [] }
        return cards.map { ($0.tier, $0.usage) }
    }
}

enum WidgetSources {
    static let noLimitsLine = "Run Codex once to see your limits."
    static let unreadableLine = "Couldn't read Codex's logs."
    static let notInstalledLine = SetupCopy.codexNotInstalled

    /// The windows a combined bar pairs, and the tier each source reports it under.
    static let windows: [(window: SourceBarWindow, tier: Tier)] = [(.fiveHour, .fiveHour), (.weekly, .sevenDay)]

    static func build(_ input: WidgetSourcesInput) -> WidgetSourceLayout {
        let codex = input.demo ? demoReading(now: input.now) : input.codex
        var shown: [AgentSource] = []
        if input.claudeOn { shown.append(.claude) }
        if input.codexOn { shown.append(.codex) }
        guard !shown.isEmpty else { return WidgetSourceLayout() }

        if input.compact {
            let lead = shown.contains(input.primary) ? input.primary : shown[0]
            return WidgetSourceLayout(sections: [section(lead, input: input, codex: codex, header: false, compact: true)])
        }
        let loneClaude = shown == [.claude]
        let sections = shown.map { section($0, input: input, codex: codex, header: !loneClaude, compact: false) }
        let combined = shown.count >= 2 ? combinedBars(input: input, codex: codex) : []
        return WidgetSourceLayout(combined: combined, sections: sections)
    }

    // MARK: Sections

    private static func section(_ source: AgentSource, input: WidgetSourcesInput, codex: CodexWidgetReading,
                                header: Bool, compact: Bool) -> WidgetSourceSection {
        let collapsed = header && input.collapsed.contains(source)
        let head = header ? self.header(source, input: input, codex: codex, collapsed: collapsed) : nil
        if collapsed { return WidgetSourceSection(source: source, header: head, content: .collapsed) }
        switch source {
        case .codex:
            return WidgetSourceSection(source: source, header: head, content: codexContent(codex, compact: compact))
        default:
            let rows = UsageViewModel.visibleTiers(input.claudeUsage, hidden: input.hidden, compact: compact)
            let cards = rows.map { WidgetSourceCard(tier: $0.tier, usage: $0.usage, label: $0.tier.label, spokenLabel: $0.tier.label) }
            return WidgetSourceSection(source: source, header: head, content: .cards(cards))
        }
    }

    private static func header(_ source: AgentSource, input: WidgetSourcesInput, codex: CodexWidgetReading,
                               collapsed: Bool) -> WidgetSourceHeader {
        let usage = source == .codex ? codex.usage : input.claudeUsage
        var asOf: String?
        if source == .codex, codex.usage != nil, let at = codex.asOf {
            asOf = "as of " + asOfWhen(at, now: input.now, calendar: input.calendar, locale: input.locale)
        }
        return WidgetSourceHeader(source: source, name: source.displayName, colorHex: input.settings.color(of: source),
                                  collapsed: collapsed, asOf: asOf,
                                  stale: source == .codex && codex.usage != nil && codex.freshTiers.isEmpty,
                                  summary: summary(usage))
    }

    private static func codexContent(_ codex: CodexWidgetReading, compact: Bool) -> WidgetSourceContent {
        guard let usage = codex.usage else { return .empty(emptyLine(codex.emptyState)) }
        var cards = windows.compactMap { w -> WidgetSourceCard? in
            guard let t = usage.tiers[w.tier], t.utilization != nil else { return nil }
            return WidgetSourceCard(tier: w.tier, usage: t, stale: !codex.freshTiers.contains(w.tier),
                                    label: w.window.label, spokenLabel: "Codex " + w.window.markWord)
        }
        if compact, let top = cards.max(by: { ($0.usage.utilization ?? 0) < ($1.usage.utilization ?? 0) }) {
            cards = [top]
        }
        return cards.isEmpty ? .empty(noLimitsLine) : .cards(cards)
    }

    static func emptyLine(_ state: CodexUsageService.EmptyState?) -> String {
        switch state {
        case .notInstalled: notInstalledLine
        case .couldNotRead: unreadableLine
        case .noLimitsYet, nil: noLimitsLine
        }
    }

    /// "5h 40% · wk 12%": what a collapsed section says in one line (Windows' `SummaryOf`).
    static func summary(_ usage: UsageResponse?) -> String {
        var parts: [String] = []
        if let v = usage?.tiers[.fiveHour]?.utilization { parts.append("5h \(Int(v))%") }
        if let v = usage?.tiers[.sevenDay]?.utilization { parts.append("wk \(Int(v))%") }
        return parts.joined(separator: " · ")
    }

    // MARK: Combined bars

    /// One bar per window both sources report; a stale Codex still joins, dimmed.
    private static func combinedBars(input: WidgetSourcesInput, codex: CodexWidgetReading) -> [WidgetCombinedBar] {
        let when = codex.asOf.map { asOfWhen($0, now: input.now, calendar: input.calendar, locale: input.locale) }
        return windows.compactMap { w in
            var points: [SourceBarPoint] = []
            if let p = input.claudeUsage?.tiers[w.tier]?.utilization {
                points.append(SourceBarPoint(source: .claude, percent: p, colorHex: input.settings.color(of: .claude)))
            }
            if let p = codex.usage?.tiers[w.tier]?.utilization {
                points.append(SourceBarPoint(source: .codex, percent: p, stale: !codex.freshTiers.contains(w.tier),
                                             colorHex: input.settings.color(of: .codex), asOf: when))
            }
            guard points.count >= 2 else { return nil }
            return WidgetCombinedBar(window: w.window,
                                     layout: SourceBar.layout(primary: input.primary, window: w.window, points: points))
        }
    }

    // MARK: As of

    /// "3:42 PM" today, "yesterday 3:42 PM", else "Nov 9, 3:42 PM": a time alone reads as today. The
    /// time is the locale's short time.
    static func asOfWhen(_ date: Date, now: Date, calendar: Calendar = .current, locale: Locale = .current) -> String {
        let time = DateFormatter()
        time.locale = locale
        time.timeZone = calendar.timeZone
        time.dateStyle = .none
        time.timeStyle = .short
        let t = time.string(from: date)
        let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: date),
                                           to: calendar.startOfDay(for: now)).day ?? 0
        if days <= 0 { return t }
        if days == 1 { return "yesterday " + t }
        let day = DateFormatter()
        day.locale = locale
        day.timeZone = calendar.timeZone
        day.setLocalizedDateFormatFromTemplate("MMMd")
        return day.string(from: date) + ", " + t
    }

    // MARK: Demo

    /// Demo mode's Codex: the sample numbers, every window fresh, as of now. Never reads `~/.codex`.
    static func demoReading(now: Date) -> CodexWidgetReading {
        CodexWidgetReading(usage: DemoMode.codexUsage(now: now), asOf: now, freshTiers: [.fiveHour, .sevenDay])
    }
}

/// Which source sections are collapsed, one key per source in the app defaults:
/// `sources.<id>.collapsed`. Only a toggle writes, so a Mac that never collapses writes nothing.
enum SourceCollapse {
    static func key(_ source: AgentSource) -> String { "sources.\(source.rawValue).collapsed" }

    static func load(from store: DefaultsStore = UserDefaults.standard) -> Set<AgentSource> {
        Set(AgentSource.withLimits.filter { (store.object(forKey: key($0)) as? Bool) == true })
    }

    static func save(_ source: AgentSource, collapsed: Bool, to store: DefaultsStore = UserDefaults.standard) {
        store.set(collapsed, forKey: key(source))
    }
}

/// What the widget needs about sources that isn't in the view model: the setup's switches, the
/// sources' colors and Primary, and the collapse state. AppDelegate reloads it whenever the setup or
/// the usage changes; a chevron toggles it here and saves at once.
@Observable
@MainActor
final class WidgetSourcesState {
    private(set) var setup: SetupSettings?
    private(set) var settings: AgentSourceSettings
    private(set) var collapsed: Set<AgentSource>
    /// Called after a toggle, so the panel fits its new height.
    @ObservationIgnored var onChange: (() -> Void)?
    @ObservationIgnored private let store: DefaultsStore
    @ObservationIgnored private let loadSetup: () -> SetupSettings?

    init(store: DefaultsStore = UserDefaults.standard, loadSetup: @escaping () -> SetupSettings? = { SetupStore.load() }) {
        self.store = store
        self.loadSetup = loadSetup
        setup = loadSetup()
        settings = AgentSourceSettings.load(from: store)
        collapsed = SourceCollapse.load(from: store)
    }

    func reload() {
        let s = loadSetup()
        if s != setup { setup = s }
        let st = AgentSourceSettings.load(from: store)
        if st != settings { settings = st }
        let c = SourceCollapse.load(from: store)
        if c != collapsed { collapsed = c }
    }

    func toggle(_ source: AgentSource) {
        let now = !collapsed.contains(source)
        SourceCollapse.save(source, collapsed: now, to: store)
        if now { collapsed.insert(source) } else { collapsed.remove(source) }
        onChange?()
    }

    var codexOn: Bool { settings.isOn(.codex, setup: setup) }
    var primary: AgentSource { settings.primary(setup: setup) }
}
