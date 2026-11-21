import Foundation
import Testing
@testable import Sanduhr

/// Item 12 (agent sources 2.46): the Local burn views show every agent. The footer's agent pieces
/// (Windows `AgentBurnDeltasTests`), the strip's stack and accessible name, the project rows'
/// shares, the Sessions tab's agent filter and label, and the CSV's `agent` column. With Claude
/// Code alone every text reads as it did in 2.45.0. Synthetic vaults only.
@Suite("Local burn shows every agent")
struct LocalBurnAgentsTests {
    typealias B = VaultBed
    static func day(_ s: String) -> CCLocalDay { CCLocalDay(key: s)! }
    static let d1 = day("2026-11-12"), d2 = day("2026-11-13")

    // MARK: Footer pieces (AgentBurnDeltasTests)

    @Test func theFirstObservationHasNoBeforeSoShowsNothing() {
        var d = AgentBurnDeltas()
        d.observe(.codex, day: Self.d1, todayTotal: 5000)
        #expect(d.footerPieces() == "")
    }

    @Test func eachAgentShowsTheChangeSinceTheIngestBeforeInAgentOrder() {
        var d = AgentBurnDeltas()
        d.observe(.antigravity, day: Self.d1, todayTotal: 100)
        d.observe(.codex, day: Self.d1, todayTotal: 1000)
        d.observe(.antigravity, day: Self.d1, todayTotal: 1100)
        d.observe(.codex, day: Self.d1, todayTotal: 4000)
        #expect(d.footerPieces() == " · Codex +3.0k · Agy +1.0k")
    }

    @Test func nothingNewIsLeftOut() {
        var d = AgentBurnDeltas()
        d.observe(.codex, day: Self.d1, todayTotal: 1000)
        d.observe(.codex, day: Self.d1, todayTotal: 1000)
        #expect(d.footerPieces() == "")
        d.observe(.antigravity, day: Self.d1, todayTotal: 10)
        d.observe(.antigravity, day: Self.d1, todayTotal: 510)
        #expect(d.footerPieces() == " · Agy +500")
    }

    @Test func atADayChangeTheEarlierTotalCountsAsZero() {
        var d = AgentBurnDeltas()
        d.observe(.codex, day: Self.d1, todayTotal: 90_000)
        d.observe(.codex, day: Self.d2, todayTotal: 2000)
        #expect(d.footerPieces() == " · Codex +2.0k")
    }

    @Test func aForgottenAgentDisappearsAndItsNextObservationIsAFirstOne() {
        var d = AgentBurnDeltas()
        d.observe(.codex, day: Self.d1, todayTotal: 1000)
        d.observe(.codex, day: Self.d1, todayTotal: 3000)
        d.forget(.codex)
        #expect(d.footerPieces() == "")
        d.observe(.codex, day: Self.d1, todayTotal: 9000)
        #expect(d.footerPieces() == "")
        d.observe(.codex, day: Self.d1, todayTotal: 10_000)
        #expect(d.footerPieces() == " · Codex +1.0k")
    }

    @Test func anIngestForgetsTheAgentsThatAreNoLongerIngesting() {
        var d = AgentBurnDeltas()
        d.observe(active: [.codex, .antigravity], day: Self.d1) { $0 == .codex ? 100 : 50 }
        d.observe(active: [.codex, .antigravity], day: Self.d1) { $0 == .codex ? 400 : 80 }
        #expect(d.footerPieces() == " · Codex +300 · Agy +30")
        d.observe(active: [.codex], day: Self.d1) { _ in 700 }      // Antigravity switched off
        #expect(d.footerPieces() == " · Codex +300")
        d.observe(active: [], day: Self.d1) { _ in 0 }               // nothing ingesting (demo, all off)
        #expect(d.footerPieces() == "")
    }

    @Test func theFooterLineCarriesThePiecesAfterTheModeAndNoneInDemo() {
        let cst = TimeZone(identifier: "America/Chicago")!
        let at = VaultTime.parse("2026-11-20T22:10:00Z")!      // 4:10 PM in Chicago
        #expect(FooterText.make(updated: at, mode: "Float", agentPieces: "", demo: false, timeZone: cst)
                == "Updated 4:10 PM · Float")
        #expect(FooterText.make(updated: at, mode: "Float", agentPieces: " · Codex +3.0k · Agy +1.0k", demo: false, timeZone: cst)
                == "Updated 4:10 PM · Float · Codex +3.0k · Agy +1.0k")
        #expect(FooterText.make(updated: at, mode: "Pinned", agentPieces: " · Codex +3.0k", demo: true, timeZone: cst)
                == "Updated 4:10 PM · Pinned")
        #expect(FooterText.make(updated: nil, mode: "Float", agentPieces: " · Codex +3.0k", demo: false) == "")
    }

    @Test func todaysAgentTotalComesFromTheAgentsRootOnly() {
        let b = B()
        defer { b.cleanUp() }
        Self.saveAgent(b, .codex, [("2026-11-13", 400, "api~aaaaaaaa")])
        Self.saveAgent(b, .antigravity, [("2026-11-12", 70, "web~bbbbbbbb")])
        VaultReaderTests.saveRollup(b, ".claude", "2026-11", [("2026-11-13", 9_999, "api~cccccccc", "")])
        let t = AgentBurnToday.totals([.codex, .antigravity], reader: VaultReader(store: b.store), today: Self.d2)
        #expect(t == [.codex: 400, .antigravity: 0])
    }

    // MARK: Shares and names

    @Test func oneAgentReadsAsTodayAndAnotherAloneIsNamed() {
        #expect(AgentShares.describe(1_200_000, [.claude: 1_200_000]) == "1.2M")
        #expect(AgentShares.describe(13_000_000, [.codex: 13_000_000, .claude: 0]) == "13.0M (Codex)")
        #expect(AgentShares.describe(700_000, [.antigravity: 700_000]) == "700k (Antigravity)")
        #expect(AgentShares.describe(500, [:]) == "500")
        #expect(AgentShares.describe(1_200_000, [.claude: 900_000, .codex: 300_000]) == "1.2M (Claude Code 900k, Codex 300k)")
    }

    @Test func theStripNameGivesEachAgentsThirtyDayTotal() {
        #expect(AgentShares.stripName([.claude: 41_000_000, .codex: 6_200_000, .antigravity: 1_100_000])
                == "30 days, Claude Code 41.0M, Codex 6.2M, Antigravity 1.1M")
        #expect(AgentShares.stripName([.codex: 6_200_000, .claude: 0]) == "30 days, Codex 6.2M")
    }

    // MARK: Overview

    static let today = day("2026-07-12")
    static let now = VaultTime.parse("2026-07-12T18:05:00Z")!
    static let plain: (String) -> String = UsagePageTests.plainName

    static func window(_ days: [(String, Int64)], project: String) -> VaultWindow {
        var w = VaultWindow()
        for (d, t) in days {
            let k = day(d)
            w.byDay[k] = t
            w.byDayInput[k] = t / 2
            w.byDayOutput[k] = t - t / 2
        }
        w.byProjectName[project] = days.reduce(0) { $0 + $1.1 }
        return w
    }

    @Test func claudeAloneIsTheOverviewOf245() {
        let live = [Self.today: UsagePageTests.live(300), Self.day("2026-07-10"): UsagePageTests.live(100)]
        let o = UsageOverview.build(today: Self.today, now: Self.now, calendar: UsagePageTests.cst, live: live, record: nil,
                                    name: Self.plain)
        #expect(!o.stacked)
        #expect(o.agentTotals.isEmpty)
        #expect(o.stripName == "Claude Code tokens, last 30 days")
        #expect(o.strip.allSatisfy { $0.agents.isEmpty && $0.total == $0.tokens })
        #expect(o.projects == [UsageRanked(name: "api", total: 400)])
        #expect(o.projects[0].valueText == "400")
        #expect(o.windowTotal == 400)
        #expect(o.today == 300)
        // The same build with the agents argument given and empty is the same value.
        let again = UsageOverview.build(today: Self.today, now: Self.now, calendar: UsagePageTests.cst, live: live,
                                        record: nil, agents: [:], name: Self.plain)
        #expect(again == o)
    }

    @Test func agentsStackEachDayAndTheNameGivesThreeAgentTotals() {
        let live = [Self.today: UsagePageTests.live(300), Self.day("2026-07-10"): UsagePageTests.live(100)]
        let agents: [AgentSource: VaultWindow] = [
            .codex: Self.window([("2026-07-10", 50), ("2026-07-12", 200)], project: "api"),
            .antigravity: Self.window([("2026-07-12", 70)], project: "web"),
        ]
        let o = UsageOverview.build(today: Self.today, now: Self.now, calendar: UsagePageTests.cst, live: live, record: nil,
                                    agents: agents, name: Self.plain)
        let byDay = Dictionary(uniqueKeysWithValues: o.strip.map { ($0.day, $0) })
        #expect(byDay[Self.today]?.tokens == 300)
        #expect(byDay[Self.today]?.total == 570)
        #expect(byDay[Self.today]?.segments.map(\.agent) == [.claude, .codex, .antigravity])
        #expect(byDay[Self.today]?.segments.map(\.tokens) == [300, 200, 70])
        #expect(byDay[Self.day("2026-07-10")]?.segments.map(\.agent) == [.claude, .codex])
        #expect(o.stacked)
        #expect(o.agentTotals == [.claude: 400, .codex: 250, .antigravity: 70])
        #expect(o.stripName == "30 days, Claude Code 400, Codex 250, Antigravity 70")
        #expect(o.windowTotal == 720)
        #expect(o.today == 570)
        #expect(o.todayInput == 200 + 100 + 35)          // Claude's 2/3 of 300, Codex's half of 200, Antigravity's half of 70
        #expect(o.todayOutput == 100 + 100 + 35)
    }

    @Test func oneAgentOtherThanClaudeStillNamesItsTotal() {
        let agents: [AgentSource: VaultWindow] = [.codex: Self.window([("2026-07-12", 6_200_000)], project: "api")]
        let o = UsageOverview.build(today: Self.today, now: Self.now, calendar: UsagePageTests.cst, live: [:], record: nil,
                                    agents: agents, name: Self.plain)
        #expect(o.stripName == "30 days, Codex 6.2M")
    }

    @Test func projectRowsGiveAgentSharesAndLeaveClaudeOnlyRowsPlain() {
        let live = [Self.today: UsagePageTests.live(900_000, cwd: "/p/api"),
                    Self.day("2026-07-11"): UsagePageTests.live(500_000, cwd: "/p/web")]
        let agents: [AgentSource: VaultWindow] = [
            .codex: Self.window([("2026-07-12", 300_000)], project: "api"),
            .antigravity: Self.window([("2026-07-10", 13_000_000)], project: "solo"),
        ]
        let o = UsageOverview.build(today: Self.today, now: Self.now, calendar: UsagePageTests.cst, live: live, record: nil,
                                    agents: agents, name: Self.plain)
        let text = Dictionary(uniqueKeysWithValues: o.projects.map { ($0.name, $0.valueText) })
        #expect(text["api"] == "1.2M (Claude Code 900k, Codex 300k)")
        #expect(text["web"] == "500k")
        #expect(text["solo"] == "13.0M (Antigravity)")
    }

    @Test func anAgentDayIsNeverNoRecord() {
        var facts = UsageRecordFacts(lastIngest: VaultTime.parse("2026-07-12T18:00:00Z"))
        facts.covered = []
        let agents: [AgentSource: VaultWindow] = [.codex: Self.window([("2026-07-08", 40)], project: "api")]
        let o = UsageOverview.build(today: Self.today, now: Self.now, calendar: UsagePageTests.cst, live: [:], record: facts,
                                    agents: agents, name: Self.plain)
        let byDay = Dictionary(uniqueKeysWithValues: o.strip.map { ($0.day, $0) })
        #expect(byDay[Self.day("2026-07-08")]?.noRecord == false)
        #expect(byDay[Self.day("2026-07-07")]?.noRecord == true)
    }

    // MARK: Through the readers

    static func saveAgent(_ b: VaultBed, _ agent: AgentSource, _ days: [(day: String, total: Int64, project: String)]) {
        b.store.saveMeta(agent.rawValue, VaultRootMeta(since: days.first?.day ?? "", lastIngestTs: "2026-07-12T18:00:00.000000+00:00",
                                                       agent: agent.rawValue))
        for month in Set(days.map { String($0.day.prefix(7)) }) {
            VaultReaderTests.saveRollup(b, agent.rawValue, month,
                                        days.filter { $0.day.hasPrefix(month) }.map { ($0.day, $0.total, $0.project, "") })
        }
    }

    @Test func theLoaderReadsAgentRootsForAnAccountWithAFolderAndWithout() {
        let b = B()
        defer { b.cleanUp() }
        Self.saveAgent(b, .codex, [("2026-07-12", 4_000, "api~aaaaaaaa")])
        let reader = VaultReader(store: b.store)
        #expect(reader.agentRoots() == [.codex])

        // No Claude Code folder: the agent's rows alone.
        let none = UsagePageLoader.overview(.noFolder, names: .names, live: nil, vault: reader, now: Self.now,
                                            calendar: UsagePageTests.cst)
        #expect(none?.mode == .agentsOnly)
        #expect(none?.status == nil)
        #expect(none?.today == 4_000)
        #expect(none?.stripName == "30 days, Codex 4.0k")
        #expect(none?.projects.map(\.valueText) == ["4.0k (Codex)"])

        // A folder linked: Claude Code's live day and the agent's, combined.
        b.session("u1", root: ".claude-work", [B.line("2026-07-12T15:00:00Z")])
        let folder = b.home.at(".claude-work")
        let some = UsagePageLoader.overview(.live(folder: folder), names: .names, live: CCLogReader(root: folder),
                                            vault: reader, now: Self.now, calendar: UsagePageTests.cst,
                                            displayName: { CCProjectName.displayName($0, dirHoldsGit: { _ in false }) })
        #expect(some?.agentTotals[.codex] == 4_000)
        let combined: Int64 = 150 + 4_000
        #expect(some?.today == combined)

        // Nothing for an agent that has no root: no overview without a folder, as in 2.45.0.
        let empty = B()
        defer { empty.cleanUp() }
        #expect(UsagePageLoader.overview(.noFolder, names: .names, live: nil, vault: VaultReader(store: empty.store),
                                         now: Self.now, calendar: UsagePageTests.cst) == nil)
    }

    // MARK: Ledger

    static func session(_ uuid: String, root: String, agent: AgentSource, name: String = "api") -> VaultSessionInfo {
        var s = UsagePageTests.session(uuid, root: root, key: "\(name)~11111111", name: name,
                                       first: "2026-07-10T10:00:00Z", last: "2026-07-10T11:00:00Z",
                                       days: [("2026-07-10", 600)])
        s.agent = agent
        return s
    }

    static let today710 = day("2026-07-10")

    @Test func theAgentFilterShowsOnlyWithMoreThanOneAgent() {
        let one = UsageLedger.rows([Self.session("u1", root: ".claude", agent: .claude)], scope: .all, today: Self.today710)
        #expect(!UsageLedger.showsAgentFilter(one))
        #expect(!UsageLedger.showsAgentLabels(one))
        #expect(UsageLedger.agentsPresent(one) == [.claude])

        let codexOnly = UsageLedger.rows([Self.session("u2", root: "codex", agent: .codex)], scope: .all, today: Self.today710)
        #expect(!UsageLedger.showsAgentFilter(codexOnly))
        #expect(UsageLedger.showsAgentLabels(codexOnly))          // the row names its agent even alone

        let three = UsageLedger.rows([Self.session("u1", root: ".claude", agent: .claude),
                                      Self.session("u3", root: "antigravity", agent: .antigravity),
                                      Self.session("u2", root: "codex", agent: .codex, name: "web")],
                                     scope: .all, today: Self.today710)
        #expect(UsageLedger.showsAgentFilter(three))
        #expect(UsageLedger.agentsPresent(three) == [.claude, .codex, .antigravity])
        #expect(UsageLedger.filtered(three, agent: nil).count == 3)
        #expect(UsageLedger.filtered(three, agent: .codex).map(\.session.uuid) == ["u2"])
        #expect(UsageLedger.filtered(three, agent: .claude).map(\.session.uuid) == ["u1"])
    }

    @Test func aRowsSpokenTextNamesItsAgentOnlyWhenLabelsShow() {
        let row = UsageLedger.rows([Self.session("u2", root: "codex", agent: .codex)], scope: .all, today: Self.today710)[0]
        let at = VaultTime.parse("2026-07-10T11:01:00Z")!
        #expect(UsageLedger.spoken(row, now: at, showsAgent: false) == "api, 600 tokens, just now")
        #expect(UsageLedger.spoken(row, now: at, showsAgent: true) == "api, Codex, 600 tokens, just now")
        #expect(UsageLedger.recordText(row, recordTitle: ".claude-work") == "codex")
        let claude = UsageLedger.rows([Self.session("u1", root: ".claude", agent: .claude)], scope: .all, today: Self.today710)[0]
        #expect(UsageLedger.recordText(claude, recordTitle: ".claude-work") == ".claude-work")
    }

    @Test func theReaderReportsEachRowsAgentFromItsRootsMeta() {
        let b = B()
        defer { b.cleanUp() }
        let row = VaultReaderTests.row("api~aaaaaaaa", "2026-07-10T10:00:00.000000+00:00", "2026-07-10T11:00:00.000000+00:00",
                                       continuation: false, [("2026-07-10", 600)])
        VaultReaderTests.saveShard(b, ".claude", "2026-07", [("c1", row)])
        VaultReaderTests.saveShard(b, "codex", "2026-07", [("x1", row)])
        VaultReaderTests.saveShard(b, "antigravity", "2026-07", [("g1", row)])
        b.store.saveMeta(".claude", VaultRootMeta(since: "2026-07-01"))
        b.store.saveMeta("codex", VaultRootMeta(since: "2026-07-01", agent: "codex"))
        b.store.saveMeta("antigravity", VaultRootMeta(since: "2026-07-01", agent: "antigravity"))
        let reader = VaultReader(store: b.store)
        let all = reader.readSessions([".claude", "codex", "antigravity"])
        #expect(Dictionary(uniqueKeysWithValues: all.map { ($0.uuid, $0.agent) }) == ["c1": .claude, "x1": .codex, "g1": .antigravity])
        // The page's read: the record (if any) and every agent root with a record.
        #expect(Set(UsagePageLoader.sessions(recordID: nil, vault: reader).map(\.uuid)) == ["x1", "g1"])
        #expect(Set(UsagePageLoader.sessions(recordID: ".claude", vault: reader).map(\.uuid)) == ["c1", "x1", "g1"])
    }

    // MARK: CSV

    @Test func theCsvAppendsTheAgentColumnLast() {
        #expect(VaultLedgerCsv.header == "session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models,agent\r\n")
        let rows = UsageLedger.rows([Self.session("u1", root: ".claude", agent: .claude),
                                     Self.session("u2", root: "codex", agent: .codex, name: "web")],
                                    scope: .all, today: Self.today710)
        let csv = UsageLedger.csv(UsageLedger.sorted(rows, by: .project, descending: false), recordTitle: ".claude-work")
        #expect(csv.rowCount == 2)
        #expect(csv.text == VaultLedgerCsv.header
                + "u1,.claude-work,api,2026-07-10T10:00:00Z,2026-07-10T11:00:00Z,600,600,claude-fable-5:600,Claude Code\r\n"
                + "u2,codex,web,2026-07-10T10:00:00Z,2026-07-10T11:00:00Z,600,600,claude-fable-5:600,Codex\r\n")
    }

    @Test func aClaudeOnlyCsvKeepsEveryOlderColumnInPlace() {
        let rows = UsageLedger.rows([Self.session("u1", root: ".claude", agent: .claude)], scope: .all, today: Self.today710)
        let line = UsageLedger.csv(rows, recordTitle: ".claude").text.components(separatedBy: "\r\n")[1]
        #expect(line.components(separatedBy: ",").prefix(8).joined(separator: ",")
                == "u1,.claude,api,2026-07-10T10:00:00Z,2026-07-10T11:00:00Z,600,600,claude-fable-5:600")
        #expect(line.hasSuffix(",Claude Code"))
    }
}
