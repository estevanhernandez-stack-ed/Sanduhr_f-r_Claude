import Foundation
import Testing
@testable import Sanduhr

/// 2.48 item 4: the Usage page's Trends tab includes the Codex and Antigravity Vault roots the way
/// Overview does. Weekly bars stack by agent, the accessible names give each agent's total, the top
/// projects carry agent shares, and with Claude Code alone Trends reads exactly as 2.47.1.
/// Synthetic vaults only.
@Suite("Trends shows every agent")
struct TrendsAgentsTests {
    static func day(_ s: String) -> CCLocalDay { CCLocalDay(key: s)! }
    static let today = day("2026-07-15")      // a Wednesday; the current week starts 07-13

    static func week(_ start: String, _ total: Int64, current: Bool = false, gap: Bool = false) -> VaultWeek {
        VaultWeek(weekStart: day(start), total: total, isCurrent: current, hasNoRecordGap: gap)
    }

    static let claude = [
        week("2026-06-29", 400), week("2026-07-06", 0), week("2026-07-13", 90, current: true),
    ]

    // MARK: Claude alone is 2.47.1

    @Test func claudeAloneIsTheTrendsOf2471() {
        let top = [(name: "api", total: Int64(490))]
        let plain = UsageTrends.build(weeks: Self.claude, top: top, birth: Self.day("2026-07-01"), today: Self.today)
        let again = UsageTrends.build(weeks: Self.claude, agents: [:], top: top, birth: Self.day("2026-07-01"), today: Self.today)
        #expect(again == plain)
        #expect(!plain.stacked)
        #expect(plain.agentTotals.isEmpty)
        #expect(plain.chartName == "Claude Code tokens by week")
        #expect(plain.bars.map(\.total) == [400, 0, 90])
        #expect(plain.bars.allSatisfy { $0.agents.isEmpty && $0.segments.count <= 1 })
        #expect(plain.bars.map(\.valueText) == ["400", "0", "90"])
        #expect(plain.top == [UsageRanked(name: "api", total: 490)])
        #expect(plain.top[0].valueText == "490")
    }

    @Test func anAgentRootWithNoTokensInTheSpanChangesNothing() {
        let idle = [Self.week("2026-06-29", 0), Self.week("2026-07-06", 0), Self.week("2026-07-13", 0, current: true)]
        let top = [(name: "api", total: Int64(490))]
        let plain = UsageTrends.build(weeks: Self.claude, top: top, birth: nil, today: Self.today)
        let with = UsageTrends.build(weeks: Self.claude, agents: [.codex: idle], top: top, birth: nil, today: Self.today)
        #expect(with == plain)
    }

    // MARK: Stacking

    @Test func weeksWithAgentRowsStackClaudeFirstThenEachAgentInOrder() {
        let codex = [Self.week("2026-06-29", 100), Self.week("2026-07-06", 250), Self.week("2026-07-13", 0, current: true)]
        let agy = [Self.week("2026-06-29", 0), Self.week("2026-07-06", 70), Self.week("2026-07-13", 10, current: true)]
        let t = UsageTrends.build(weeks: Self.claude, agents: [.antigravity: agy, .codex: codex], top: [],
                                  birth: nil, today: Self.today)
        #expect(t.stacked)
        #expect(t.bars.map(\.total) == [500, 320, 100])
        #expect(t.bars[0].segments.map(\.agent) == [.claude, .codex])
        #expect(t.bars[0].segments.map(\.tokens) == [400, 100])
        #expect(t.bars[1].segments.map(\.agent) == [.codex, .antigravity])      // Claude's week is empty
        #expect(t.bars[1].segments.map(\.tokens) == [250, 70])
        #expect(t.bars[2].segments.map(\.agent) == [.claude, .antigravity])
        #expect(t.bars[2].segments.map(\.tokens) == [90, 10])
        #expect(t.bars.map(\.kind) == [.bar, .bar, .bar])
        #expect(t.bars.last?.isCurrent == true)
        #expect(t.maxTotal == 500)
        #expect(t.agentTotals == [.claude: 490, .codex: 350, .antigravity: 80])
    }

    @Test func aWeekOnlyAnAgentTouchedIsABarNotNoRecord() {
        // The Claude record began later: its early weeks are gaps, the agent's tokens are real.
        let claude = [Self.week("2026-06-29", 0, gap: true), Self.week("2026-07-06", 50), Self.week("2026-07-13", 0, current: true)]
        let codex = [Self.week("2026-06-29", 300, gap: true), Self.week("2026-07-06", 0), Self.week("2026-07-13", 0, current: true)]
        let t = UsageTrends.build(weeks: claude, agents: [.codex: codex], top: [], birth: nil, today: Self.today)
        #expect(t.bars.map(\.kind) == [.bar, .bar, .zero])
        #expect(t.bars[0].total == 300)
        #expect(t.bars[0].partialGap)            // every source has a gap that week
    }

    @Test func aCoveredSourceMakesAnEmptyWeekARealZero() {
        let claude = [Self.week("2026-07-06", 0, gap: true), Self.week("2026-07-13", 5, current: true)]
        let codex = [Self.week("2026-07-06", 0), Self.week("2026-07-13", 40, current: true)]
        let t = UsageTrends.build(weeks: claude, agents: [.codex: codex], top: [], birth: nil, today: Self.today)
        #expect(t.bars.map(\.kind) == [.zero, .bar])
    }

    @Test func withNoClaudeRecordTheAgentsAloneMakeTheBars() {
        let codex = [Self.week("2026-07-06", 250), Self.week("2026-07-13", 40, current: true)]
        let t = UsageTrends.build(weeks: [], agents: [.codex: codex], top: [], birth: Self.today.adding(days: -1), today: Self.today)
        #expect(t.bars.map(\.total) == [250, 40])
        #expect(t.bars.map { $0.segments.map(\.agent) } == [[.codex], [.codex]])
        #expect(t.agentTotals == [.codex: 290])
        #expect(t.chartName == "Tokens by week, Codex 290")
        // The fresh-record note speaks of Claude Code's logs; with no Claude record it stays quiet.
        #expect(t.freshNote == nil)
    }

    // MARK: Accessible names

    @Test func theChartNameGivesEachAgentsTotalAndEachBarSaysItsShares() {
        let codex = [Self.week("2026-06-29", 1_000), Self.week("2026-07-06", 0), Self.week("2026-07-13", 0, current: true)]
        let agy = [Self.week("2026-06-29", 0), Self.week("2026-07-06", 0), Self.week("2026-07-13", 2_000, current: true)]
        let t = UsageTrends.build(weeks: Self.claude, agents: [.codex: codex, .antigravity: agy], top: [],
                                  birth: nil, today: Self.today)
        #expect(t.chartName == "Tokens by week, Claude Code 490, Codex 1.0k, Antigravity 2.0k")
        #expect(t.bars.map(\.valueText) == ["1.4k (Claude Code 400, Codex 1.0k)", "0",
                                            "2.1k (Claude Code 90, Antigravity 2.0k)"])
    }

    @Test func anotherAgentAloneInAWeekIsNamed() {
        let codex = [Self.week("2026-06-29", 0), Self.week("2026-07-06", 5_000), Self.week("2026-07-13", 0, current: true)]
        let claude = [Self.week("2026-06-29", 0), Self.week("2026-07-06", 0), Self.week("2026-07-13", 0, current: true)]
        let t = UsageTrends.build(weeks: claude, agents: [.codex: codex], top: [], birth: nil, today: Self.today)
        #expect(t.bars[1].valueText == "5.0k (Codex)")
    }

    // MARK: Top projects

    @Test func topProjectsMergeByNameWithAgentShares() {
        let claude: [String: Int64] = ["api": 900_000, "web": 500_000]
        var codex = VaultWindow()
        codex.byProjectName = ["api": 300_000]
        var agy = VaultWindow()
        agy.byProjectName = ["solo": 13_000_000]
        let top = UsageOverview.rankedProjects(claude, agents: [.codex: codex, .antigravity: agy], top: UsageTrends.topCount)
        let text = Dictionary(uniqueKeysWithValues: top.map { ($0.name, $0.valueText) })
        #expect(text["api"] == "1.2M (Claude Code 900k, Codex 300k)")
        #expect(text["web"] == "500k")
        #expect(text["solo"] == "13.0M (Antigravity)")
    }

    // MARK: Through the readers

    static func saveAgent(_ b: VaultBed, _ agent: AgentSource, _ days: [(day: String, total: Int64, project: String)],
                          covered: [(String, String)] = [("2026-06-22", "2026-07-15")]) {
        var meta = VaultReaderTests.meta(days.first?.day ?? "", covered)
        meta.agent = agent.rawValue
        b.store.saveMeta(agent.rawValue, meta)
        for month in Set(days.map { String($0.day.prefix(7)) }) {
            VaultReaderTests.saveRollup(b, agent.rawValue, month,
                                        days.filter { $0.day.hasPrefix(month) }.map { ($0.day, $0.total, $0.project, "") })
        }
    }

    static let id = "abcdef0123456789"

    static func claudeBed() -> VaultBed {
        let b = VaultBed()
        VaultReaderTests.saveRollup(b, id, "2026-07", [("2026-07-01", 300, "api~aaaaaaaa", ""),
                                                      ("2026-07-14", 50, "web~bbbbbbbb", "")])
        b.store.saveMeta(id, VaultReaderTests.meta("2026-07-01", [("2026-07-01", "2026-07-05"), ("2026-07-13", "2026-07-15")]))
        return b
    }

    @Test func aClaudeOnlyVaultReadsThroughTheLoaderAsIt2471Did() {
        let b = Self.claudeBed()
        defer { b.cleanUp() }
        let reader = VaultReader(store: b.store)
        let t = UsagePageLoader.trends(recordID: Self.id, weeks: 4, vault: reader, today: Self.today)
        #expect(t.bars.map(\.kind) == [.noRecord, .bar, .noRecord, .bar])
        #expect(t.bars.map(\.partialGap) == [false, true, false, false])
        #expect(t.bars.map(\.total) == [0, 300, 0, 50])
        #expect(!t.stacked)
        #expect(t.chartName == "Claude Code tokens by week")
        #expect(t.top == [UsageRanked(name: "api", total: 300), UsageRanked(name: "web", total: 50)])
        #expect(t.footer == "History kept since July 1, 2026")
        // The same read the old way: weeks from the reader, top from the reader.
        let weeks = reader.readWeeks([Self.id], weeks: 4, today: Self.today)
        let top = reader.topProjects([Self.id], from: weeks[0].weekStart, toExclusive: Self.today.adding(days: 1),
                                     top: UsageTrends.topCount)
        #expect(t == UsageTrends.build(weeks: weeks, top: top, birth: reader.birthDate([Self.id]), today: Self.today))
    }

    @Test func anAgentRootWithAnIdleSpanLeavesTheLoadedTrendsEqual() {
        let b = Self.claudeBed()
        defer { b.cleanUp() }
        let before = UsagePageLoader.trends(recordID: Self.id, weeks: 4, vault: VaultReader(store: b.store), today: Self.today)
        // A Codex root whose only tokens are months before the span.
        Self.saveAgent(b, .codex, [("2026-03-02", 9_999, "old~cccccccc")])
        let after = UsagePageLoader.trends(recordID: Self.id, weeks: 4, vault: VaultReader(store: b.store), today: Self.today)
        #expect(after.bars == before.bars)
        #expect(after.top == before.top)
        #expect(after.footer == before.footer)
        #expect(!after.stacked)
    }

    @Test func theLoaderStacksAnAgentRootIntoTheClaudeWeeks() {
        let b = Self.claudeBed()
        defer { b.cleanUp() }
        Self.saveAgent(b, .codex, [("2026-07-01", 700, "api~dddddddd"), ("2026-07-14", 20, "api~dddddddd")])
        Self.saveAgent(b, .antigravity, [("2026-07-08", 60, "lab~eeeeeeee")])
        let t = UsagePageLoader.trends(recordID: Self.id, weeks: 4, vault: VaultReader(store: b.store), today: Self.today)
        #expect(t.bars.map(\.total) == [0, 1_000, 60, 70])
        #expect(t.bars.map(\.kind) == [.zero, .bar, .bar, .bar])   // the agents cover the span
        #expect(t.bars[1].segments.map(\.agent) == [.claude, .codex])
        #expect(t.bars[2].segments.map(\.agent) == [.antigravity])
        #expect(t.agentTotals == [.claude: 350, .codex: 720, .antigravity: 60])
        let text = Dictionary(uniqueKeysWithValues: t.top.map { ($0.name, $0.valueText) })
        #expect(text["api"] == "1.0k (Claude Code 300, Codex 720)")
        #expect(text["lab"] == "60 (Antigravity)")
        #expect(text["web"] == "50")
    }

    @Test func withNoClaudeRecordTheLoaderReadsTheAgentRootsAlone() {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveAgent(b, .codex, [("2026-07-08", 800, "api~dddddddd"), ("2026-07-14", 20, "api~dddddddd")])
        let reader = VaultReader(store: b.store)
        let t = UsagePageLoader.trends(recordID: nil, weeks: 4, vault: reader, today: Self.today)
        #expect(t.bars.map(\.total) == [0, 0, 800, 20])
        #expect(t.agentTotals == [.codex: 820])
        #expect(t.top.map(\.valueText) == ["820 (Codex)"])
        #expect(t.footer == "History kept since July 8, 2026")
        // Nothing at all: no bars.
        let empty = VaultBed()
        defer { empty.cleanUp() }
        let none = UsagePageLoader.trends(recordID: nil, weeks: 4, vault: VaultReader(store: empty.store), today: Self.today)
        #expect(none.bars.isEmpty)
    }

    // MARK: Colors

    @Test func theStackUsesTheSourceColors() {
        var settings = AgentSourceSettings()
        settings.colors[.codex] = "#112233"
        let codex = [Self.week("2026-06-29", 100), Self.week("2026-07-06", 0), Self.week("2026-07-13", 0, current: true)]
        let t = UsageTrends.build(weeks: Self.claude, agents: [.codex: codex], top: [], birth: nil, today: Self.today)
        let stack = AgentStripStack(trends: t, settings: settings)
        #expect(stack?.name == t.chartName)
        #expect(stack?.colors[.codex] == "#112233")
        #expect(stack?.colors[.claude] == settings.color(of: .claude))
        let plain = UsageTrends.build(weeks: Self.claude, top: [], birth: nil, today: Self.today)
        #expect(AgentStripStack(trends: plain, settings: settings) == nil)
    }
}
