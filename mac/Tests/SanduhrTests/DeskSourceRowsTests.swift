import Foundation
import Testing
@testable import Sanduhr

/// The Desk meter pieces' rows by source (agent sources, item 8), pure. With only Claude on (or Codex
/// off) every piece draws today's rows; Codex alone draws its two windows; combined joins the windows
/// both report into the `SourceBar` layout. Made-up numbers only; nothing reads `~/.codex`.

private let now = Date(timeIntervalSince1970: 1_800_000_000)

private func tier(_ util: Double, in seconds: TimeInterval = 3600) -> TierUsage {
    TierUsage(utilization: util, resetsAt: ISO8601.stamp(now.addingTimeInterval(seconds), fractional: false))
}

private let claudeUsage = UsageResponse(tiers: [.fiveHour: tier(40), .sevenDay: tier(63, in: 86_400), .sevenDaySonnet: tier(18)])
private let codexUsage = UsageResponse(tiers: [.fiveHour: tier(30, in: 7200), .sevenDay: tier(70, in: 3 * 86_400)])

private func input(codexOn: Bool = true, usage: UsageResponse? = codexUsage, fresh: Set<Tier> = [.fiveHour, .sevenDay],
                   primary: AgentSource = .claude) -> DeskSourceInput {
    DeskSourceInput(codexOn: codexOn,
                    codex: CodexWidgetReading(usage: usage, asOf: now, freshTiers: fresh, emptyState: nil),
                    primary: primary, colors: [.claude: "#E0875A", .codex: "#2FB5A3"])
}

private func claudeRows(_ usage: UsageResponse? = claudeUsage) -> [DeskMeterRow] { DeskMeterRow.rows(from: usage, now: now) }

@Suite("Desk meter source: which source a piece draws")
struct DeskSourceResolveTests {
    private func resolve(_ source: DeskMeterSource, _ input: DeskSourceInput, present: Bool = true, numbers: Bool = true) -> DeskMeterSource {
        DeskSourceRows.resolve(source, input: input, claudePresent: present, claudeNumbers: numbers)
    }

    @Test(arguments: DeskMeterSource.allCases)
    func withCodexOffEveryPieceIsTodaysPiece(_ source: DeskMeterSource) {
        #expect(resolve(source, .claudeOnly) == .claude)
        #expect(resolve(source, input(codexOn: false)) == .claude)
        #expect(resolve(source, input(codexOn: false), present: false, numbers: false) == .claude)
    }

    @Test func withBothOnCombinedJoinsAndTheOthersAreTheirOwn() {
        #expect(resolve(.combined, input()) == .combined)
        #expect(resolve(.claude, input()) == .claude)
        #expect(resolve(.codex, input()) == .codex)
    }

    @Test func combinedWithNoClaudeToDrawIsCodexAlone() {
        #expect(resolve(.combined, input(), present: false, numbers: false) == .codex)
    }

    @Test func combinedWithASignInOrNoCodexNumbersIsClaudeAsToday() {
        #expect(resolve(.combined, input(), present: true, numbers: false) == .claude)
        #expect(resolve(.combined, input(usage: nil)) == .claude)
        #expect(resolve(.combined, input(usage: UsageResponse())) == .claude)
        #expect(resolve(.combined, input(usage: nil), present: false, numbers: false) == .claude)
    }

    @Test func aCodexPieceWithNoCodexNumbersDrawsNothing() {
        #expect(resolve(.codex, input(usage: nil)) == .codex)
        #expect(DeskSourceRows.codexRows(input(usage: nil), now: now).isEmpty)
        #expect(DeskSourceRows.codexLine(input(usage: nil)) == nil)
    }
}

@Suite("Desk meter source: Codex alone")
struct DeskCodexRowsTests {
    @Test func codexRowsAreTheTwoWindowsLabelledSoTheyCantBeTakenForClaudes() {
        let rows = DeskSourceRows.codexRows(input(), now: now)
        #expect(rows.map(\.tier) == [.fiveHour, .sevenDay])
        #expect(rows.map(\.label) == ["Codex session (5hr)", "Codex weekly"])
        #expect(rows.map(\.percent) == [30, 70])
        #expect(rows.map(\.fill) == [0.3, 0.7])
        #expect(rows.allSatisfy { $0.pace == nil && !$0.warning && $0.layout == nil })
        #expect(rows.allSatisfy { $0.reset == resetDateTimeStr(codexUsage.tiers[$0.tier]?.resetsAt, now: now) })
    }

    @Test func codexNeverWarnsEvenNearlyFull() {
        let hot = UsageResponse(tiers: [.fiveHour: tier(97, in: 4 * 3600), .sevenDay: tier(99, in: 6 * 86_400)])
        #expect(DeskSourceRows.codexRows(input(usage: hot), now: now).allSatisfy { !$0.warning })
    }

    @Test func eachWindowIsStaleOnItsOwn() {
        let rows = DeskSourceRows.codexRows(input(fresh: [.sevenDay]), now: now)
        #expect(rows.map(\.stale) == [true, false])
        #expect(!DeskSourceRows.codexStale(input(fresh: [.sevenDay])))
        #expect(DeskSourceRows.codexStale(input(fresh: [])))
    }

    @Test func aWindowCodexDoesNotReportIsLeftOut() {
        let weekOnly = UsageResponse(tiers: [.sevenDay: tier(12)])
        #expect(DeskSourceRows.codexRows(input(usage: weekOnly), now: now).map(\.tier) == [.sevenDay])
        #expect(DeskSourceRows.codexLine(input(usage: weekOnly)) == "Codex wk 12%")
    }

    @Test func theLineReadsLikeTheNotchsShortMeters() {
        #expect(DeskSourceRows.codexLine(input()) == "Codex 5h 30% · wk 70%")
        #expect(DeskSourceRows.codexLine(input(codexOn: false)) == nil)
    }

    @Test func theViewCarriesTheNameTheGaugesAndTheStaleness() {
        let view = DeskSourceRows.codexView(input(fresh: []), now: now)
        #expect(view.name == "Codex")
        #expect(view.stale)
        #expect(view.gauges.map(\.label) == ["session", "week"])
        #expect(view.gauges.allSatisfy { $0.stale })
        #expect(view.line == "Codex 5h 30% · wk 70%")
    }

    @Test func demoCodexNumbersDrawAsAnyOther() {
        let demo = CodexWidgetReading(usage: DemoMode.codexUsage(now: now), asOf: now, freshTiers: [.fiveHour, .sevenDay])
        let rows = DeskSourceRows.codexRows(DeskSourceInput(codexOn: true, codex: demo), now: now)
        #expect(rows.map(\.percent) == [27, 71])
    }
}

@Suite("Desk meter source: combined")
struct DeskCombinedRowsTests {
    @Test func theSharedWindowsJoinAndOtherRowsStayAsTheyWere() {
        let claude = claudeRows()
        let rows = DeskSourceRows.combinedRows(claude: claude, claudeUsage: claudeUsage, input: input(), now: now)
        #expect(rows.map(\.tier) == claude.map(\.tier))
        #expect(rows.map(\.label) == ["Session (5hr)", "Weekly", "Weekly — Sonnet"])
        #expect(rows[0].layout != nil && rows[1].layout != nil)
        // A model's weekly no other source reports is today's row, with no layout.
        #expect(rows[2] == claude[2])
        #expect(rows[2].layout == nil)
    }

    @Test func claudePrimaryKeepsClaudesNumberAndPaceAndDropsTheWarning() {
        var hot = claudeRows()
        hot[0].warning = true
        let rows = DeskSourceRows.combinedRows(claude: hot, claudeUsage: claudeUsage, input: input(primary: .claude), now: now)
        #expect(rows[0].percent == 40)
        #expect(rows[0].pace == claudeRows()[0].pace)
        #expect(rows[0].warning == false)
        let layout = rows[0].layout
        #expect(layout?.primary?.source == .claude)
        #expect(layout?.primary?.percent == 40)
        #expect(layout?.beyond?.source == nil)       // Codex at 30 is behind Claude's 40: a tick
        #expect(layout?.ticks.map(\.source) == [.codex])
        // The weekly: Codex at 70 is past Claude's 63, so it continues the bar.
        #expect(rows[1].layout?.beyond?.source == .codex)
    }

    @Test func codexPrimaryLeadsWithCodexsNumberResetAndNoPace() {
        let rows = DeskSourceRows.combinedRows(claude: claudeRows(), claudeUsage: claudeUsage,
                                               input: input(primary: .codex), now: now)
        #expect(rows[0].percent == 30)
        #expect(rows[0].fill == 0.3)
        #expect(rows[0].pace == nil)
        #expect(rows[0].reset == resetDateTimeStr(codexUsage.tiers[.fiveHour]?.resetsAt, now: now))
        #expect(rows[0].layout?.primary?.source == .codex)
        #expect(rows[1].percent == 70)
    }

    @Test func theLayoutsAreSourceBarsOwnWithTheSourcesColors() {
        let rows = DeskSourceRows.combinedRows(claude: claudeRows(), claudeUsage: claudeUsage, input: input(), now: now)
        let expected = SourceBar.layout(primary: .claude, window: .weekly, points: [
            SourceBarPoint(source: .claude, percent: 63, colorHex: "#E0875A"),
            SourceBarPoint(source: .codex, percent: 70, colorHex: "#2FB5A3", asOf: nil)])
        #expect(rows[1].layout?.primary == expected.primary)
        #expect(rows[1].layout?.beyond == expected.beyond)
        #expect(rows[1].layout?.primary?.colorHex == "#E0875A")
        #expect(rows[1].layout?.beyond?.colorHex == "#2FB5A3")
    }

    @Test func aStaleCodexWindowJoinsDimmedWithItsAsOf() {
        let rows = DeskSourceRows.combinedRows(claude: claudeRows(), claudeUsage: claudeUsage,
                                               input: input(fresh: [.sevenDay]), now: now)
        let session = rows[0].layout?.ticks.first { $0.source == .codex }
        #expect(session?.stale == true)
        #expect(session?.opacity == SourceBar.staleOpacity)
        #expect(session?.name.contains("As of") == true)
        let week = rows[1].layout?.beyond
        #expect(week?.stale == false)
    }

    @Test func aHiddenClaudeLimitIsNotJoined() {
        let weekOnly = claudeRows().filter { $0.tier == .sevenDay }
        let rows = DeskSourceRows.combinedRows(claude: weekOnly, claudeUsage: claudeUsage, input: input(), now: now)
        #expect(rows.map(\.tier) == [.sevenDay])
    }

    @Test func aWindowOnlyOneSourceReportsStaysClaudes() {
        let weekOnly = input(usage: UsageResponse(tiers: [.sevenDay: tier(70)]))
        let rows = DeskSourceRows.combinedRows(claude: claudeRows(), claudeUsage: claudeUsage, input: weekOnly, now: now)
        #expect(rows[0] == claudeRows()[0])
        #expect(rows[1].layout != nil)
    }

    @Test func theViewsGaugesCarryTheLayoutsForTheClaudePiece() {
        let view = DeskSourceRows.combinedView(meters: claudeRows(), all: claudeRows(), claudeUsage: claudeUsage,
                                               input: input(), now: now)
        #expect(view.gauges.map(\.label) == ["session", "week"])
        #expect(view.gauges.allSatisfy { $0.layout != nil })
        #expect(view.rows.map(\.label) == ["Session (5hr)", "Weekly", "Weekly — Sonnet"])
        #expect(view.line == nil && view.name == nil && !view.stale)
    }
}

@Suite("Desk meter source: one source is exactly today's Desk")
struct DeskOneSourceTests {
    @MainActor
    private func model(_ source: DeskSourceInput) -> DeskModel {
        let m = DeskModel()
        m.updateSources(source, now: now)
        m.update(DeskUsage(usage: claudeUsage, fetchedAt: now), now: now)
        return m
    }

    @MainActor
    @Test func aClaudeOnlyModelNeverLeavesTodaysPath() {
        let m = model(.claudeOnly)
        for source in DeskMeterSource.allCases { #expect(m.drawn(source) == .claude) }
        #expect(m.meters == DeskMeterRow.rows(from: claudeUsage, now: now))
        #expect(m.meters.allSatisfy { $0.layout == nil && !$0.stale })
        #expect(m.codexView == DeskSourceView())
        #expect(m.combinedView == DeskSourceView())
        #expect(m.claudeGauges.allSatisfy { $0.layout == nil && !$0.stale })
    }

    @MainActor
    @Test func codexOffWithACodexWordIsStillTodaysPiece() {
        let m = model(input(codexOn: false))
        #expect(m.drawn(.codex) == .claude)
        #expect(m.drawn(.combined) == .claude)
        #expect(m.codexView == DeskSourceView())
    }

    @MainActor
    @Test func theClaudeMetersAndGaugesAreTheSameWithCodexOnOrOff() {
        let off = model(.claudeOnly)
        let on = model(input())
        #expect(on.meters == off.meters)
        #expect(on.claudeLine == off.claudeLine)
        #expect(on.claudeGauges == off.claudeGauges)
        #expect(on.drawn(.claude) == .claude)
        #expect(on.drawn(.combined) == .combined)
        #expect(on.drawn(.codex) == .codex)
        #expect(on.combinedView.rows.first?.layout != nil)
        #expect(on.codexView.rows.map(\.percent) == [30, 70])
    }

    @MainActor
    @Test func combinedWithASignInNeededIsClaudeAsToday() {
        let m = DeskModel()
        m.updateSources(input(), now: now)
        m.update(DeskUsage(usage: nil, signInNeeded: true), now: now)
        #expect(m.drawn(.combined) == .claude)
        #expect(m.drawn(.codex) == .codex)
    }

    @MainActor
    @Test func aCodexOnlySetupDrawsCombinedAsCodex() {
        let m = DeskModel()
        m.updateSources(input(), now: now)
        m.update(DeskUsage(usage: nil, usageOff: true), now: now)
        #expect(m.drawn(.combined) == .codex)
        #expect(m.drawn(.claude) == .claude)
    }

    @MainActor
    @Test func turningCodexOffAgainClearsTheViews() {
        let m = model(input())
        #expect(m.codexView != DeskSourceView())
        m.updateSources(.claudeOnly, now: now)
        #expect(m.codexView == DeskSourceView())
        #expect(m.combinedView == DeskSourceView())
        #expect(m.drawn(.combined) == .claude)
    }
}

@Suite("Desk meter source: the geometry each style draws")
struct DeskSourceGeometryTests {
    private func layout(claude: Double, codex: Double, primary: AgentSource = .claude, codexStale: Bool = false) -> SourceBarLayout {
        SourceBar.layout(primary: primary, window: .weekly, points: [
            SourceBarPoint(source: .claude, percent: claude, colorHex: "#E0875A"),
            SourceBarPoint(source: .codex, percent: codex, stale: codexStale, colorHex: "#2FB5A3")])
    }

    @Test func aSingleFillRingArcIsTodaysTrim() {
        for fill in [0.0, 0.0005, 0.01, 0.3, 0.5, 1] {
            let seg = DeskMeterStyleModel.ringSegment(from: 0, to: fill)
            let trim = DeskMeterStyleModel.ringTrim(fill)
            #expect((seg?.to ?? 0) == trim)
            #expect((seg?.from ?? 0) == 0)
        }
    }

    @Test func theContinuationArcStartsWhereThePrimaryEnds() {
        let l = layout(claude: 40, codex: 70)
        let primary = DeskMeterStyleModel.ringSegment(from: l.primary!.from, to: l.primary!.to)
        let beyond = DeskMeterStyleModel.ringSegment(from: l.beyond!.from, to: l.beyond!.to)
        #expect(primary?.from == 0)
        #expect(beyond?.from == primary?.to)
        #expect(beyond?.to == 0.7 * 270 / 360)
    }

    @Test func aStretchTooShortToDrawIsNoArc() {
        #expect(DeskMeterStyleModel.ringSegment(from: 0.5, to: 0.5005) == nil)
        #expect(DeskMeterStyleModel.ringSegment(from: 0.7, to: 0.4) == nil)
        #expect(DeskMeterStyleModel.ringSegment(from: .nan, to: .infinity) == nil)
    }

    @Test func aTickStandsAtThePaceMarksAngle() {
        #expect(DeskMeterStyleModel.ringTickAngle(at: 0) == 135)
        #expect(DeskMeterStyleModel.ringTickAngle(at: 0.5) == 270)
        #expect(DeskMeterStyleModel.ringTickAngle(at: 1.4) == 405)
    }

    @Test func theTicksCellsTakeThePrimarysColorThenTheContinuations() {
        let cells = DeskMeterStyleModel.sourceCells(layout(claude: 40, codex: 70))
        #expect(cells.count == 10)
        #expect(cells.prefix(4).allSatisfy { $0.colorHex == "#E0875A" })
        #expect(cells[4...6].allSatisfy { $0.colorHex == "#2FB5A3" })
        #expect(cells[7...].allSatisfy { $0.colorHex == nil })
    }

    @Test func aSourceBehindThePrimaryLeavesAllItsCellsTheirs() {
        let cells = DeskMeterStyleModel.sourceCells(layout(claude: 70, codex: 30))
        #expect(cells.filter { $0.colorHex == "#E0875A" }.count == 7)
        #expect(cells.allSatisfy { $0.colorHex != "#2FB5A3" })
    }

    @Test func aSmallReadingStillLightsACell() {
        let cells = DeskMeterStyleModel.sourceCells(layout(claude: 3, codex: 0))
        #expect(cells.filter { $0.colorHex != nil }.count == 1)
        #expect(DeskMeterStyleModel.sourceCells(layout(claude: 0, codex: 0)).allSatisfy { $0.colorHex == nil })
    }

    @Test func aStaleContinuationCellsAreMarkedStale() {
        let cells = DeskMeterStyleModel.sourceCells(layout(claude: 40, codex: 70, codexStale: true))
        #expect(cells[5].stale)
        #expect(!cells[0].stale)
    }

    @Test func aSingleSegmentLayoutCoversTheSameCellsAsTheFillDoes() {
        for fill in [0.0, 0.03, 0.25, 0.5, 0.95, 1.0] {
            let single = SourceBar.layout(primary: .claude, window: .fiveHour,
                                          points: [SourceBarPoint(source: .claude, percent: fill * 100)])
            #expect(single.isSingleFill)
            let lit = DeskMeterStyleModel.sourceCells(single).filter { $0.colorHex != nil }.count
            #expect(lit == DeskMeterStyleModel.litTicks(fill))
        }
    }

    @Test func theBarGeometryIsSourceBarsOwn() {
        let l = layout(claude: 40, codex: 70)
        let primary = SourceBar.span(l.primary!, in: 200)
        let beyond = SourceBar.span(l.beyond!, in: 200)
        #expect(primary.x == 0 && primary.width == 80)
        #expect(beyond.x == 80 && abs(beyond.width - 60) < 0.0001)
        #expect(l.ticks.count == 1 && l.ticks[0].at == 0.7)
        #expect(SourceBar.tickX(at: 0.7, in: 200) == 139)
        #expect(SourceBar.staleOpacity == 0.55)
    }
}

@Suite("Desk meter source: what the Desk is told")
struct DeskSourceInputTests {
    private let claudeOnly = SetupSettings(desktop: true, usage: ["claude": true])
    private let both = SetupSettings(desktop: true, usage: ["claude": true, "codex": true])
    private let codexOnly = SetupSettings(desktop: true, usage: ["claude": false, "codex": true])
    private let reading = CodexWidgetReading(usage: codexUsage, asOf: now, freshTiers: [.fiveHour])

    @Test func aMacWithOnlyClaudeOnIsToldNothing() {
        var sources = AgentSourceSettings.default
        sources.savedPrimary = .codex
        sources.colors[.codex] = "#112233"
        #expect(DeskSourceInput.make(setup: claudeOnly, sources: sources, codex: reading, demo: false, now: now) == .claudeOnly)
        #expect(DeskSourceInput.make(setup: nil, sources: sources, codex: reading, demo: true, now: now) == .claudeOnly)
        #expect(!DeskSourceInput.claudeOnly.hasCodexLimits)
    }

    @Test func withCodexOnItCarriesTheReadingPrimaryAndColors() {
        var sources = AgentSourceSettings.default
        sources.savedPrimary = .codex
        sources.colors[.codex] = "#112233"
        let input = DeskSourceInput.make(setup: both, sources: sources, codex: reading, demo: false, now: now)
        #expect(input.codexOn && input.hasCodexLimits)
        #expect(input.codex == reading)
        #expect(input.primary == .codex)
        #expect(input.color(of: .codex) == "#112233")
        #expect(input.color(of: .claude) == AgentSource.claude.defaultColor)
    }

    @Test func aCodexOnlySetupLeadsWithCodex() {
        let input = DeskSourceInput.make(setup: codexOnly, sources: .default, codex: reading, demo: false, now: now)
        #expect(input.primary == .codex)
    }

    @Test func demoModeShowsTheSampleCodexNotTheServicesReading() {
        let input = DeskSourceInput.make(setup: both, sources: .default, codex: .none, demo: true, now: now)
        #expect(input.codex.usage == DemoMode.codexUsage(now: now))
        #expect(input.codex.freshTiers == [.fiveHour, .sevenDay])
        #expect(input.hasCodexLimits)
    }

    @Test func codexOnWithNoReadingHasNoLimitsToDraw() {
        let input = DeskSourceInput.make(setup: both, sources: .default, codex: .none, demo: false, now: now)
        #expect(input.codexOn && !input.hasCodexLimits)
        #expect(DeskMeterSourceSettings.codexOn(setup: both, sources: .default))
        #expect(!DeskMeterSourceSettings.codexOn(setup: claudeOnly, sources: .default))
    }
}
