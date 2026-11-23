import Foundation
import Testing
@testable import Sanduhr

/// The widget with two sources (agent sources, item 6): the rows the widget builds, pure. A Mac with
/// only Claude on builds exactly what 2.45.0 built (`UsageViewModel.visibleTiers`); with Codex on
/// too, two combined bars and a section per source. Made-up numbers only; nothing reads `~/.codex`.

private let now = Date(timeIntervalSince1970: 1_800_000_000)

private func tier(_ util: Double, in seconds: TimeInterval = 3600) -> TierUsage {
    TierUsage(utilization: util, resetsAt: ISO8601.stamp(now.addingTimeInterval(seconds), fractional: false))
}

private let claudeSamples: [UsageResponse?] = [
    nil,
    UsageResponse(),
    UsageResponse(tiers: [.fiveHour: tier(42)]),
    UsageResponse(tiers: [.fiveHour: tier(42), .sevenDay: tier(63, in: 86_400), .sevenDaySonnet: tier(18)]),
    UsageResponse(tiers: [.fiveHour: tier(10), .sevenDay: tier(90), .sevenDayOpus: tier(95),
                          .iguanaNecktie: TierUsage(utilization: nil, resetsAt: nil)]),
]

private let codexUsage = UsageResponse(tiers: [.fiveHour: tier(30, in: 7200), .sevenDay: tier(70, in: 3 * 86_400)])

private func codex(_ usage: UsageResponse? = codexUsage, fresh: Set<Tier> = [.fiveHour, .sevenDay],
                   asOf: Date? = now, empty: CodexUsageService.EmptyState? = nil,
                   history: HistoryStore.History = [:], historyOn: Bool = true) -> CodexWidgetReading {
    CodexWidgetReading(usage: usage, asOf: asOf, freshTiers: fresh, emptyState: empty,
                       history: history, historyOn: historyOn)
}

/// A made-up `@codex` history: `values` per tier, an hour apart, ending at `now`.
private func madeUpHistory(five: [Double], week: [Double]) -> HistoryStore.History {
    func points(_ values: [Double]) -> [HistoryStore.Point] {
        values.enumerated().map { i, v in
            HistoryStore.Point(t: ISO8601.stamp(now.addingTimeInterval(-3600 * Double(values.count - 1 - i)), fractional: false), v: v)
        }
    }
    return [Tier.fiveHour.rawValue: points(five), Tier.sevenDay.rawValue: points(week)]
}

private func input(claude: UsageResponse? = claudeSamples[3], claudeOn: Bool = true, codexOn: Bool = true,
                   codex reading: CodexWidgetReading = codex(), primary: AgentSource = .claude,
                   collapsed: Set<AgentSource> = [], compact: Bool = false, demo: Bool = false,
                   hidden: Set<Tier> = []) -> WidgetSourcesInput {
    var cal = Calendar(identifier: .gregorian)
    cal.timeZone = TimeZone(identifier: "UTC")!
    return WidgetSourcesInput(claudeOn: claudeOn, claudeUsage: claude, hidden: hidden, codexOn: codexOn,
                              codex: reading, primary: primary, settings: .default, collapsed: collapsed,
                              compact: compact, demo: demo, now: now, calendar: cal,
                              locale: Locale(identifier: "en_US_POSIX"))
}

/// The locale's short time may put a narrow no-break space before AM; the tests read it as a space.
private func spaced(_ text: String?) -> String? { text?.replacingOccurrences(of: "\u{202F}", with: " ") }

private func tiers(_ section: WidgetSourceSection?) -> [Tier] {
    guard case .cards(let cards)? = section?.content else { return [] }
    return cards.map(\.tier)
}

@Suite("Widget sources: Claude only is 2.45.0")
struct WidgetSourcesClaudeOnlyTests {
    @Test(arguments: [false, true])
    func claudeOnlyBuildsTodaysRows(compact: Bool) {
        for usage in claudeSamples {
            for hidden in [Set<Tier>(), [.sevenDay]] {
                let built = WidgetSources.build(input(claude: usage, codexOn: false, compact: compact, hidden: hidden))
                let today = UsageViewModel.visibleTiers(usage, hidden: hidden, compact: compact)
                #expect(built.isClaudeOnly)
                #expect(built.combined.isEmpty)
                #expect(built.sections.count == 1)
                #expect(built.sections.first?.header == nil)
                #expect(built.claudeRows.map(\.tier) == today.map(\.tier))
                #expect(built.claudeRows.map(\.usage) == today.map(\.usage))
            }
        }
    }

    @Test func aCollapsedClaudeAloneStillDrawsItsCards() {
        let built = WidgetSources.build(input(codexOn: false, collapsed: [.claude]))
        #expect(tiers(built.sections.first) == [.fiveHour, .sevenDay, .sevenDaySonnet])
    }

    @Test func codexDataWithCodexOffChangesNothing() {
        let built = WidgetSources.build(input(codexOn: false, codex: codex()))
        #expect(built.isClaudeOnly)
        #expect(built.sections.map(\.source) == [.claude])
    }
}

@Suite("Widget sources: Claude and Codex")
struct WidgetSourcesTwoTests {
    @Test func twoCombinedBarsThenASectionPerSource() {
        let built = WidgetSources.build(input())
        #expect(!built.isClaudeOnly)
        #expect(built.combined.map(\.window) == [.fiveHour, .weekly])
        let weekly = built.combined[1].layout
        #expect(weekly.primary?.source == .claude)
        #expect(weekly.primary?.percent == 63)
        #expect(weekly.beyond?.source == .codex)
        #expect(weekly.beyond?.percent == 70)
        #expect(weekly.beyond?.colorHex == AgentSource.codex.defaultColor)
        #expect(built.sections.map(\.source) == [.claude, .codex])
        #expect(tiers(built.sections[0]) == [.fiveHour, .sevenDay, .sevenDaySonnet])
        #expect(tiers(built.sections[1]) == [.fiveHour, .sevenDay])
    }

    @Test func headersNameTheSourceWithItsColorAndTheCodexAsOf() {
        let built = WidgetSources.build(input())
        let claude = built.sections[0].header
        let codexHeader = built.sections[1].header
        #expect(claude?.name == "Claude")
        #expect(claude?.colorHex == AgentSource.claude.defaultColor)
        #expect(claude?.asOf == nil)
        #expect(codexHeader?.name == "Codex")
        #expect(codexHeader?.colorHex == AgentSource.codex.defaultColor)
        #expect(spaced(codexHeader?.asOf) == "as of 8:00 AM")
        #expect(codexHeader?.stale == false)
        #expect(codexHeader?.collapsed == false)
    }

    @Test func codexCardsUseTheWindowLabelsAndSayCodexAloud() {
        let built = WidgetSources.build(input())
        guard case .cards(let cards) = built.sections[1].content else { Issue.record("no cards"); return }
        #expect(cards.map(\.label) == ["Session (5hr)", "Weekly"])
        #expect(cards.map(\.spokenLabel) == ["Codex session", "Codex weekly"])
        #expect(cards.allSatisfy { !$0.stale })
        #expect(cards.map(\.usage) == [codexUsage.tiers[.fiveHour]!, codexUsage.tiers[.sevenDay]!])
    }

    @Test func codexPrimaryFillsTheCombinedBars() {
        let built = WidgetSources.build(input(primary: .codex))
        #expect(built.combined[0].layout.primary?.source == .codex)
        #expect(built.combined[0].layout.beyond?.source == .claude)
    }

    @Test func aWindowOnlyOneSourceHasGetsNoCombinedBar() {
        let onlyWeek = UsageResponse(tiers: [.sevenDay: tier(20)])
        let built = WidgetSources.build(input(codex: codex(onlyWeek, fresh: [.sevenDay])))
        #expect(built.combined.map(\.window) == [.weekly])
    }

    @Test func asOfIsYesterdayOrADateWhenNotToday() {
        let yesterday = WidgetSources.build(input(codex: codex(asOf: now.addingTimeInterval(-86_400))))
        #expect(spaced(yesterday.sections[1].header?.asOf) == "as of yesterday 8:00 AM")
        let older = WidgetSources.build(input(codex: codex(asOf: now.addingTimeInterval(-5 * 86_400))))
        #expect(spaced(older.sections[1].header?.asOf) == "as of Jan 10, 8:00 AM")
    }

    @Test func codexAloneKeepsItsHeaderAndHasNoCombinedBars() {
        let built = WidgetSources.build(input(claudeOn: false))
        #expect(built.combined.isEmpty)
        #expect(built.sections.map(\.source) == [.codex])
        #expect(built.sections[0].header?.name == "Codex")
    }
}

@Suite("Widget sources: a stale Codex")
struct WidgetSourcesStaleTests {
    @Test func aStaleTierDimsItsCardAndItsCombinedMark() {
        let built = WidgetSources.build(input(codex: codex(fresh: [.fiveHour])))
        guard case .cards(let cards) = built.sections[1].content else { Issue.record("no cards"); return }
        #expect(cards.map(\.stale) == [false, true])
        #expect(built.sections[1].header?.stale == false)
        let weekly = built.combined[1].layout
        #expect(weekly.beyond?.stale == true)
        #expect(spaced(weekly.beyond?.name) == "Codex weekly, 70%. As of 8:00 AM.")
        #expect(built.combined[0].layout.ticks.first?.stale == false)
    }

    @Test func everyTierStaleDimsTheHeaderToo() {
        let built = WidgetSources.build(input(codex: codex(fresh: [])))
        #expect(built.sections[1].header?.stale == true)
        #expect(built.combined.count == 2)
    }
}

@Suite("Widget sources: Codex empty states")
struct WidgetSourcesEmptyTests {
    @Test(arguments: [
        (CodexUsageService.EmptyState.notInstalled, "Codex isn't installed on this Mac."),
        (.noLimitsYet, "Run Codex once to see your limits."),
        (.couldNotRead, "Couldn't read Codex's logs."),
    ])
    func eachEmptyStateShowsItsLineAndJoinsNoBar(state: CodexUsageService.EmptyState, line: String) {
        let built = WidgetSources.build(input(codex: codex(nil, fresh: [], asOf: nil, empty: state)))
        #expect(built.sections.map(\.source) == [.claude, .codex])
        #expect(built.sections[1].content == .empty(line))
        #expect(built.sections[1].header?.asOf == nil)
        #expect(built.combined.isEmpty)
    }

    @Test func beforeTheFirstReadTheLineIsRunCodexOnce() {
        let built = WidgetSources.build(input(codex: codex(nil, fresh: [], asOf: nil, empty: nil)))
        #expect(built.sections[1].content == .empty("Run Codex once to see your limits."))
    }
}

@Suite("Widget sources: collapse")
struct WidgetSourcesCollapseTests {
    @Test func aCollapsedSectionShowsItsSummaryInTheHeader() {
        let built = WidgetSources.build(input(collapsed: [.codex]))
        #expect(built.sections[1].content == .collapsed)
        #expect(built.sections[1].header?.collapsed == true)
        #expect(built.sections[1].header?.summary == "5h 30% · wk 70%")
        #expect(built.sections[0].header?.collapsed == false)
        #expect(tiers(built.sections[0]).count == 3)
    }

    @Test func collapsingEverySourceLeavesTheCombinedBars() {
        let built = WidgetSources.build(input(collapsed: [.claude, .codex]))
        #expect(built.combined.count == 2)
        #expect(built.sections.allSatisfy { $0.content == .collapsed })
        #expect(built.claudeRows.isEmpty)
    }

    @Test func collapseIsRememberedPerSource() {
        let store = MemoryDefaults()
        #expect(SourceCollapse.load(from: store).isEmpty)
        SourceCollapse.save(.codex, collapsed: true, to: store)
        #expect(SourceCollapse.load(from: store) == [.codex])
        #expect(store.values.keys.sorted() == ["sources.codex.collapsed"])
        SourceCollapse.save(.claude, collapsed: true, to: store)
        SourceCollapse.save(.codex, collapsed: false, to: store)
        #expect(SourceCollapse.load(from: store) == [.claude])
    }
}

@Suite("Widget sources: compact")
struct WidgetSourcesCompactTests {
    @Test func compactKeepsTheHighestCardOfClaudeWhenItLeads() {
        let built = WidgetSources.build(input(compact: true))
        #expect(built.combined.isEmpty)
        #expect(built.sections.map(\.source) == [.claude])
        #expect(built.sections[0].header == nil)
        #expect(built.claudeRows.map(\.tier) == [.sevenDay])
    }

    @Test func compactKeepsTheHighestCardOfCodexWhenItLeads() {
        let built = WidgetSources.build(input(primary: .codex, compact: true))
        #expect(built.combined.isEmpty)
        #expect(built.sections.map(\.source) == [.codex])
        #expect(built.sections[0].header == nil)
        #expect(tiers(built.sections[0]) == [.sevenDay])
        #expect(built.claudeRows.isEmpty)
    }

    @Test func compactIgnoresCollapse() {
        let built = WidgetSources.build(input(collapsed: [.claude], compact: true))
        #expect(built.claudeRows.map(\.tier) == [.sevenDay])
    }
}

@Suite("Widget sources: demo")
struct WidgetSourcesDemoTests {
    @Test func demoShowsASampleCodexSectionFreshAsOfNow() {
        let built = WidgetSources.build(input(codex: codex(nil, fresh: [], asOf: nil, empty: .notInstalled), demo: true))
        let sample = DemoMode.codexUsage(now: now)
        guard case .cards(let cards) = built.sections[1].content else { Issue.record("no cards"); return }
        #expect(cards.map(\.usage) == [sample.tiers[.fiveHour]!, sample.tiers[.sevenDay]!])
        #expect(cards.allSatisfy { !$0.stale })
        #expect(spaced(built.sections[1].header?.asOf) == "as of 8:00 AM")
        #expect(built.combined.count == 2)
    }

    @Test func demoWithCodexOffShowsNoCodex() {
        let built = WidgetSources.build(input(codexOn: false, demo: true))
        #expect(built.isClaudeOnly)
    }

    @Test func theCodexSampleIsMadeUpNumbersForBothWindows() {
        let sample = DemoMode.codexUsage(now: now)
        #expect(Set(sample.tiers.keys) == [.fiveHour, .sevenDay])
        #expect(sample.tiers.values.allSatisfy { ($0.utilization ?? -1) > 0 && $0.resetsAt != nil })
    }
}

@Suite("Widget sources: Codex sparklines")
struct WidgetSourcesCodexSparklineTests {
    private func cards(_ built: WidgetSourceLayout) -> [WidgetSourceCard] {
        guard case .cards(let cards) = built.sections[1].content else { Issue.record("no cards"); return [] }
        return cards
    }

    @Test func aCodexCardCarriesItsTiersHistoryPoints() {
        let history = madeUpHistory(five: [10, 20, 30], week: [60, 65, 70])
        let built = WidgetSources.build(input(codex: codex(history: history)))
        let rows = cards(built)
        #expect(rows.map(\.tier) == [.fiveHour, .sevenDay])
        #expect(rows[0].history == [10, 20, 30])
        #expect(rows[1].history == [60, 65, 70])
    }

    @Test func aSparklineKeepsTheNewestPointsOnly() {
        let many = (0..<(HistoryStore.sparklinePoints + 10)).map(Double.init)
        let built = WidgetSources.build(input(codex: codex(history: madeUpHistory(five: many, week: [1]))))
        #expect(cards(built)[0].history == Array(many.suffix(HistoryStore.sparklinePoints)))
    }

    @Test func aTierWithNoHistoryHasNoPoints() {
        let built = WidgetSources.build(input(codex: codex(history: madeUpHistory(five: [10, 20], week: []))))
        #expect(cards(built)[0].history == [10, 20])
        #expect(cards(built)[1].history.isEmpty)
    }

    @Test func meterHistoryOffForCodexDrawsNoSparkline() {
        let history = madeUpHistory(five: [10, 20, 30], week: [60, 65, 70])
        let built = WidgetSources.build(input(codex: codex(history: history, historyOn: false)))
        #expect(cards(built).allSatisfy { $0.history.isEmpty })
    }

    @Test func noHistoryOnFileDrawsNoSparkline() {
        let built = WidgetSources.build(input())
        #expect(cards(built).allSatisfy { $0.history.isEmpty })
    }

    @Test func compactKeepsTheTopCardsPoints() {
        let history = madeUpHistory(five: [10, 20, 30], week: [60, 65, 70])
        let built = WidgetSources.build(input(codex: codex(history: history), primary: .codex, compact: true))
        guard case .cards(let rows)? = built.sections.first?.content else { Issue.record("no cards"); return }
        #expect(rows.map(\.tier) == [.sevenDay])
        #expect(rows[0].history == [60, 65, 70])
    }

    @Test func claudeCardsAreUnchangedWhateverCodexHolds() {
        let history = madeUpHistory(five: [10, 20, 30], week: [60, 65, 70])
        let with = WidgetSources.build(input(codex: codex(history: history)))
        let without = WidgetSources.build(input(codex: codex()))
        guard case .cards(let a) = with.sections[0].content, case .cards(let b) = without.sections[0].content else {
            Issue.record("no cards"); return
        }
        #expect(a == b)
        #expect(a.allSatisfy { $0.history.isEmpty })
        #expect(with.claudeRows.map(\.usage) == without.claudeRows.map(\.usage))
    }

    @Test func demoDrawsAMadeUpCodexSparklineEvenWithNothingOnFile() {
        let built = WidgetSources.build(input(codex: codex(nil, fresh: [], asOf: nil, empty: .notInstalled), demo: true))
        let rows = cards(built)
        #expect(rows.count == 2)
        for row in rows {
            #expect(row.history.count == HistoryStore.sparklinePoints)
            #expect(row.history.allSatisfy { $0 >= 0 && $0 <= 100 })
            #expect(row.history.last == DemoMode.codexUsage(now: now).tiers[row.tier]?.utilization)
        }
    }

    @Test func theDemoCodexHistoryCoversBothWindowsAndEndsAtTheSampleNumbers() {
        let h = DemoMode.codexHistory(now: now)
        #expect(Set(h.keys) == [Tier.fiveHour.rawValue, Tier.sevenDay.rawValue])
        let sample = DemoMode.codexUsage(now: now)
        for tier in [Tier.fiveHour, .sevenDay] {
            #expect(h[tier.rawValue]?.last?.v == sample.tiers[tier]?.utilization)
        }
    }
}
