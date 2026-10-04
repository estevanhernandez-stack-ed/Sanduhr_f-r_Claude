import Foundation
import Testing
@testable import Sanduhr

/// Item 48, the Claude Usage page's view models: what each account can show, project names
/// under the account's choice, the Overview's hot-day rule and degraded mode, Trends' "no
/// record" weeks, and the ledger's scope, sort, texts and CSV. Synthetic vaults and temp folders
/// only.
@Suite("Claude Usage page")
struct UsagePageTests {
    typealias B = VaultBed

    static func day(_ s: String) -> CCLocalDay { CCLocalDay(key: s)! }

    static var cst: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = B.cst
        return c
    }

    static func live(_ total: Int64, cwd: String = "/p/api", skill: String? = nil) -> CCLiveDay {
        var d = CCLiveDay()
        d.total = total
        d.input = total * 2 / 3
        d.output = total - d.input
        d.byCwd[cwd] = total
        if let skill { d.bySkill[skill] = total }
        d.byModel["claude-fable-5"] = total
        return d
    }

    static let plainName: (String) -> String = { cwd in
        UsageProjectNames.name(cwd, choice: .names, displayName: { CCProjectName.displayName($0, dirHoldsGit: { _ in false }) })
    }

    // MARK: What an account can show

    @Test func sourceFollowsTheChoices() {
        var c = AccountDataChoices()
        #expect(UsageSource.of(c) == .notTracked)
        c.activity = .live
        #expect(UsageSource.of(c) == .noFolder)
        c.folder = "/Users/x/.claude-work"
        #expect(UsageSource.of(c) == .live(folder: "/Users/x/.claude-work"))
        #expect(UsageSource.of(c).recordID == nil)
        c.activity = .record
        let id = VaultFolderID.of("/Users/x/.claude-work")
        #expect(UsageSource.of(c) == .record(folder: "/Users/x/.claude-work", id: id))
        #expect(UsageSource.of(c).recordID == id)
        c.activity = .off
        #expect(UsageSource.of(c) == .notTracked)          // a linked folder alone reads nothing
        #expect(UsageSource.of(c).folder == nil)
    }

    // MARK: Project names

    @Test func liveNamesFollowTheChoiceAndNeverResolveACode() {
        let fold: (String) -> String = { CCProjectName.displayName($0, dirHoldsGit: { _ in false }) }
        let wt = "/Users/x/Projects/api/.claude/worktrees/feature"
        #expect(UsageProjectNames.name(wt, choice: .names, displayName: fold) == "api")
        #expect(UsageProjectNames.name(wt, choice: .full, displayName: fold) == "api")
        let code = UsageProjectNames.name(wt, choice: .hidden, displayName: fold)
        #expect(code == VaultHiddenName.of("api"))
        #expect(VaultHiddenName.isHidden(code))
        #expect(!code.contains("api"))
        #expect(UsageProjectNames.name("", choice: .hidden, displayName: fold) == "(none)")
        #expect(UsageProjectNames.anyHidden(["web", code]))
        #expect(!UsageProjectNames.anyHidden(["web", "p-notacode"]))
    }

    // MARK: Overview

    @Test func hotDayRuleServesClosedDaysFromTheRecordAndTodayLive() {
        let today = Self.day("2026-07-12")
        let now = VaultTime.parse("2026-07-12T18:05:00Z")!
        var facts = UsageRecordFacts(lastIngest: VaultTime.parse("2026-07-12T18:00:00Z"))
        facts.window.byDay = [Self.day("2026-07-10"): 1000, Self.day("2026-07-11"): 500]
        facts.window.byProjectName = ["web": 1500]
        facts.window.bySkill = ["review": 200]
        facts.covered = Set((0..<29).map { today.adding(days: -29 + $0) })
        // The live reader also holds the closed days (with other numbers): they must not count.
        let live = [Self.day("2026-07-10"): Self.live(7), Self.day("2026-07-11"): Self.live(9),
                    today: Self.live(300, skill: "review")]
        let o = UsageOverview.build(today: today, now: now, calendar: Self.cst, live: live, record: facts, name: Self.plainName)
        #expect(o.mode == .record)
        #expect(o.status == nil)
        #expect(o.strip.count == 30)
        #expect(o.strip.first?.day == Self.day("2026-06-13"))
        #expect(o.strip.last?.day == today)
        #expect(o.strip.filter(\.live).map(\.day) == [today])
        #expect(o.strip.first { $0.day == Self.day("2026-07-10") }?.tokens == 1000)
        #expect(o.today == 300)
        #expect(o.todayInput == 200)
        #expect(o.todayOutput == 100)
        #expect(o.windowTotal == 1800)
        #expect(o.projects == [UsageRanked(name: "web", total: 1500), UsageRanked(name: "api", total: 300)])
        #expect(o.skills == [UsageRanked(name: "review", total: 500)])
    }

    @Test func justAfterMidnightYesterdayStaysLiveUntilThePassLands() {
        let today = Self.day("2026-07-13")
        // 23:58 CDT on the 12th; now 00:03 on the 13th: the last pass didn't close the 12th.
        let last = VaultTime.parse("2026-07-13T04:58:00Z")!
        let now = VaultTime.parse("2026-07-13T05:03:00Z")!
        #expect(UsageOverview.hotStart(lastIngest: last, today: today, calendar: Self.cst) == Self.day("2026-07-12"))
        let r = UsageOverview.closedRange(lastIngest: last, today: today, now: now, calendar: Self.cst)
        #expect(r?.from == Self.day("2026-06-14"))
        #expect(r?.toExclusive == Self.day("2026-07-12"))
        var facts = UsageRecordFacts(lastIngest: last)
        facts.window.byDay = [Self.day("2026-07-11"): 40]
        facts.covered = [Self.day("2026-07-11")]
        let live = [Self.day("2026-07-11"): Self.live(1), Self.day("2026-07-12"): Self.live(800), today: Self.live(5)]
        let o = UsageOverview.build(today: today, now: now, calendar: Self.cst, live: live, record: facts, name: Self.plainName)
        #expect(o.mode == .record)
        #expect(o.strip.filter(\.live).map(\.day) == [Self.day("2026-07-12"), today])
        // Yesterday never dips at midnight: it reads live, in full.
        #expect(o.strip.first { $0.day == Self.day("2026-07-12") }?.tokens == 800)
        #expect(o.strip.first { $0.day == Self.day("2026-07-11") }?.tokens == 40)
        #expect(o.windowTotal == 845)
    }

    @Test func aStaleRecordFallsBackToTheLiveReaderWithAStatusLine() {
        let today = Self.day("2026-07-12")
        let last = VaultTime.parse("2026-07-12T18:00:00Z")!
        var facts = UsageRecordFacts(lastIngest: last)
        facts.window.byDay = [Self.day("2026-07-10"): 1000]
        let live = [Self.day("2026-07-10"): Self.live(7), today: Self.live(3)]
        // 15 minutes is three cycles: still trusted at 15, not after.
        #expect(!UsageOverview.isDegraded(lastIngest: last, now: last.addingTimeInterval(15 * 60)))
        #expect(UsageOverview.isDegraded(lastIngest: last, now: last.addingTimeInterval(15 * 60 + 1)))
        #expect(UsageOverview.isDegraded(lastIngest: nil, now: last))
        let late = last.addingTimeInterval(20 * 60)
        #expect(UsageOverview.closedRange(lastIngest: last, today: today, now: late, calendar: Self.cst) == nil)
        let o = UsageOverview.build(today: today, now: late, calendar: Self.cst, live: live, record: facts, name: Self.plainName)
        #expect(o.mode == .paused)
        #expect(o.status == "Record paused: showing Claude Code's live logs only until the next pass.")
        #expect(o.strip.allSatisfy { $0.live })
        #expect(o.strip.allSatisfy { !$0.noRecord })
        #expect(o.windowTotal == 10)
        let never = UsageOverview.build(today: today, now: late, calendar: Self.cst, live: live,
                                        record: UsageRecordFacts(), name: Self.plainName)
        #expect(never.mode == .starting)
        #expect(never.status?.hasPrefix("Record starting") == true)
    }

    @Test func liveOnlyReadsTheLiveReaderAlone() {
        let today = Self.day("2026-07-12")
        let live = [Self.day("2026-07-01"): Self.live(10, cwd: ""), today: Self.live(5)]
        let o = UsageOverview.build(today: today, now: Date(), calendar: Self.cst, live: live, record: nil, name: Self.plainName)
        #expect(o.mode == .live)
        #expect(o.status?.hasPrefix("Live only") == true)
        #expect(o.strip.allSatisfy { $0.live })
        #expect(o.windowTotal == 15)
        #expect(o.projects.map(\.name) == ["(none)", "api"])
    }

    @Test func uncoveredClosedDaysAreNoRecordNeverZero() {
        let today = Self.day("2026-07-12")
        let now = VaultTime.parse("2026-07-12T18:05:00Z")!
        var facts = UsageRecordFacts(lastIngest: VaultTime.parse("2026-07-12T18:00:00Z"))
        facts.window.byDay = [Self.day("2026-07-08"): 70]
        facts.covered = Set((0..<5).map { Self.day("2026-07-07").adding(days: $0) })   // 07-07...07-11
        let o = UsageOverview.build(today: today, now: now, calendar: Self.cst, live: [:], record: facts, name: Self.plainName)
        let byDay = Dictionary(uniqueKeysWithValues: o.strip.map { ($0.day, $0) })
        #expect(byDay[Self.day("2026-07-06")]?.noRecord == true)     // before the record began
        #expect(byDay[Self.day("2026-07-07")]?.noRecord == false)    // covered, empty: a real zero
        #expect(byDay[Self.day("2026-07-08")]?.tokens == 70)
        #expect(byDay[today]?.noRecord == false)                      // today is live
        #expect(o.strip.filter(\.noRecord).count == 24)          // 06-13 through 07-06
    }

    @Test func hiddenNamesOnLiveDaysAreCodes() {
        let today = Self.day("2026-07-12")
        let hidden: (String) -> String = { UsageProjectNames.name($0, choice: .hidden, displayName: { CCProjectName.displayName($0, dirHoldsGit: { _ in false }) }) }
        let o = UsageOverview.build(today: today, now: Date(), calendar: Self.cst,
                                    live: [today: Self.live(5, cwd: "/Users/x/Projects/secret")], record: nil, name: hidden)
        #expect(o.projects.map(\.name) == [VaultHiddenName.of("secret")])
        #expect(!o.projects.contains { $0.name.contains("secret") })
    }

    // MARK: Overview through the readers

    /// A recorded folder with one closed day and today, ingested, then read through the loader.
    @Test func loaderMatchesTheVaultForClosedDaysAndTheLiveReaderForToday() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let closed = b.session("u1", root: ".claude-work", [B.line("2026-07-10T15:00:00Z")])
        b.session("u2", root: ".claude-work", [B.line("2026-07-12T15:00:00Z", input: 200, output: 100, skill: "review")])
        let folder = b.home.at(".claude-work")
        AccountData.link(folder, to: "Work", in: d)
        AccountData.setActivity(.record, for: "Work", in: d)
        let service = VaultServiceTests.service(b, d)
        service.ingestNow(now: B.now)
        let source = UsageSource.of(AccountData.choices(for: "Work", in: d))
        let names: (String) -> String = { CCProjectName.displayName($0, dirHoldsGit: { _ in false }) }
        let now = B.now.addingTimeInterval(300)

        func overview(_ at: Date) -> UsageOverview? {
            UsagePageLoader.overview(source, names: .names, live: CCLogReader(root: folder), vault: service.reader,
                                     now: at, calendar: Self.cst, displayName: names)
        }
        let o = overview(now)
        #expect(o?.mode == .record)
        #expect(o?.today == 300)
        #expect(o?.strip.first { $0.day == Self.day("2026-07-10") }?.tokens == 150)
        #expect(o?.windowTotal == 450)
        #expect(o?.projects == [UsageRanked(name: "api", total: 450)])
        #expect(o?.skills == [UsageRanked(name: "review", total: 300)])
        let vaultDay = service.reader.readWindow([source.recordID!], from: Self.day("2026-07-10"),
                                                 toExclusive: Self.day("2026-07-11")).total
        #expect(vaultDay == 150)

        // Claude Code deletes the old log: the closed day still comes from the record...
        try? FileManager.default.removeItem(atPath: closed)
        #expect(overview(now)?.strip.first { $0.day == Self.day("2026-07-10") }?.tokens == 150)
        // ...unless the record is stale: then it is live only, and the day is gone with its log.
        let stale = overview(B.now.addingTimeInterval(20 * 60))
        #expect(stale?.mode == .paused)
        #expect(stale?.strip.first { $0.day == Self.day("2026-07-10") }?.tokens == 0)
        #expect(stale?.today == 300)
    }

    @Test func loaderReadsNothingWithoutAFolder() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let r = UsagePageLoader.overview(.notTracked, names: .names, live: nil, vault: VaultReader(store: b.store))
        #expect(r == nil)
        #expect(UsagePageLoader.overview(.noFolder, names: .names, live: nil, vault: VaultReader(store: b.store)) == nil)
    }

    @Test func liveReaderDaysMatchItsOtherQueries() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.write(h, session: "a", [
            CCLogs.event("2026-07-10T15:00:00Z", "claude-fable-5", 100, 50, cwd: "/p/api", skill: "review"),
            CCLogs.event("2026-07-12T15:00:00Z", "claude-sonnet-5", 10, 5),
            CCLogs.event("2026-07-12T16:00:00Z", nil, 1, 1, cwd: "/p/web"),
            CCLogs.event("2026-07-12T16:30:00Z", "claude-fable-5", 0, 0, cwd: "/p/web"),
            CCLogs.event("2026-06-01T16:30:00Z", "claude-fable-5", 9, 9, cwd: "/p/web"),
        ])
        let r = CCLogs.reader(h)
        let days = r.days(from: Self.day("2026-07-01"), calendar: Self.cst)
        #expect(days.count == 2)
        let d10 = days[Self.day("2026-07-10")]
        #expect(d10?.total == 150)
        #expect(d10?.input == 100)
        #expect(d10?.output == 50)
        #expect(d10?.byCwd == ["/p/api": 150])
        #expect(d10?.bySkill == ["review": 150])
        let d12 = days[Self.day("2026-07-12")]
        #expect(d12?.total == 17)
        #expect(d12?.byCwd == ["": 15, "/p/web": 2])
        #expect(d12?.byModel == ["claude-sonnet-5": 15, "<none>": 2])
    }

    // MARK: Trends

    @Test func trendsKindsCurrentWeekAndFooter() {
        let today = Self.day("2026-07-15")
        let weeks = [
            VaultWeek(weekStart: Self.day("2026-06-22"), total: 0, isCurrent: false, hasNoRecordGap: true),
            VaultWeek(weekStart: Self.day("2026-06-29"), total: 400, isCurrent: false, hasNoRecordGap: true),
            VaultWeek(weekStart: Self.day("2026-07-06"), total: 0, isCurrent: false, hasNoRecordGap: false),
            VaultWeek(weekStart: Self.day("2026-07-13"), total: 90, isCurrent: true, hasNoRecordGap: false),
        ]
        let t = UsageTrends.build(weeks: weeks, top: [(name: "api", total: 490)], birth: Self.day("2026-07-01"), today: today)
        #expect(t.bars.map(\.kind) == [.noRecord, .bar, .zero, .bar])
        #expect(t.bars.map(\.partialGap) == [false, true, false, false])
        #expect(t.bars.map(\.isCurrent) == [false, false, false, true])
        #expect(t.maxTotal == 400)
        #expect(t.top == [UsageRanked(name: "api", total: 490)])
        #expect(t.footer == "History kept since July 1, 2026")
        #expect(t.freshNote == nil)
        let fresh = UsageTrends.build(weeks: weeks, top: [], birth: today.adding(days: -1), today: today)
        #expect(fresh.freshNote?.hasPrefix("A new record") == true)
        #expect(UsageTrends.build(weeks: [], top: [], birth: nil, today: today).footer == nil)
    }

    @Test func trendsThroughTheReaderMarkWeeksBeforeTheRecordAndGaps() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let id = "abcdef0123456789"
        VaultReaderTests.saveRollup(b, id, "2026-07", [("2026-07-01", 300, "api~aaaaaaaa", ""),
                                                      ("2026-07-14", 50, "web~bbbbbbbb", "")])
        // Covered 07-01..07-05 and 07-13..07-15: the week of 07-06 has a gap and no tokens.
        b.store.saveMeta(id, VaultReaderTests.meta("2026-07-01", [("2026-07-01", "2026-07-05"), ("2026-07-13", "2026-07-15")]))
        let t = UsagePageLoader.trends(recordID: id, weeks: 4, vault: VaultReader(store: b.store), today: Self.day("2026-07-15"))
        #expect(t.bars.map(\.weekStart) == ["2026-06-22", "2026-06-29", "2026-07-06", "2026-07-13"].map(Self.day))
        #expect(t.bars.map(\.kind) == [.noRecord, .bar, .noRecord, .bar])
        #expect(t.bars.map(\.partialGap) == [false, true, false, false])   // 06-29 and 06-30 uncovered
        #expect(t.bars.last?.isCurrent == true)
        #expect(t.top == [UsageRanked(name: "api", total: 300), UsageRanked(name: "web", total: 50)])
        #expect(t.footer == "History kept since July 1, 2026")
    }

    // MARK: Ledger

    static func session(_ uuid: String, root: String = "r", key: String = "api~11111111", name: String = "api",
                        first: String, last: String, days: [(String, Int64)],
                        models: [String: Int64]? = nil, cwd: String? = nil) -> VaultSessionInfo {
        var byDay: [String: VaultDayBucket] = [:]
        for (d, t) in days { byDay[d] = VaultDayBucket(total: t, byModel: ["claude-fable-5": t]) }
        let total = days.reduce(Int64(0)) { $0 + $1.1 }
        return VaultSessionInfo(uuid: uuid, root: root, projectKey: key, projectName: name, cwd: cwd,
                                firstTs: VaultTime.parse(first)!, lastTs: VaultTime.parse(last)!, total: total,
                                byModel: models ?? ["claude-fable-5": total], bySkill: nil, byDay: byDay,
                                cache: nil, agentCount: 0, agentTokens: 0)
    }

    static var ledgerSessions: [VaultSessionInfo] {
        [
            session("old", first: "2026-07-01T10:00:00Z", last: "2026-07-01T12:00:00Z", days: [("2026-07-01", 900_000)]),
            session("yday", key: "api~22222222", first: "2026-07-11T10:00:00Z", last: "2026-07-11T11:00:00Z",
                    days: [("2026-07-11", 800_000)]),
            session("span", key: "web~33333333", name: "web", first: "2026-07-10T10:00:00Z", last: "2026-07-12T09:00:00Z",
                    days: [("2026-07-10", 100), ("2026-07-11", 200), ("2026-07-12", 300)]),
        ]
    }

    @Test func scopesAreInclusiveDayRanges() {
        let today = Self.day("2026-07-12")
        #expect(LedgerScope.default == .week)
        func range(_ s: LedgerScope) -> [CCLocalDay] {
            let r = s.range(today: today)
            return [r.from, r.through]
        }
        #expect(range(.today) == [today, today])
        #expect(range(.yesterday) == [Self.day("2026-07-11"), Self.day("2026-07-11")])
        #expect(range(.week) == [Self.day("2026-07-06"), today])
        #expect(range(.all)[0] < Self.day("2000-01-01"))
        #expect(range(.all)[1] > Self.day("2100-01-01"))
        #expect(LedgerScope.allCases.map(\.title) == ["Today", "Yesterday", "7d", "All"])
    }

    @Test func scopedTokensComeFromByDayAndRankYesterdaysCulprit() {
        let today = Self.day("2026-07-12")
        let rows = UsageLedger.rows(Self.ledgerSessions, scope: .yesterday, today: today)
        #expect(rows.map(\.id) == ["r|old", "r|yday", "r|span"])
        #expect(rows.map(\.scoped) == [0, 800_000, 200])
        let sorted = UsageLedger.sorted(rows, by: .tokens, descending: true)
        #expect(sorted.map(\.session.uuid) == ["yday", "span", "old"])
        // All: lifetime ranks the week-old monster first.
        let all = UsageLedger.sorted(UsageLedger.rescoped(rows, scope: .all, today: today), by: .tokens, descending: true)
        #expect(all.map(\.session.uuid) == ["old", "yday", "span"])
        let todayRows = UsageLedger.rescoped(rows, scope: .today, today: today)
        #expect(todayRows.map(\.scoped) == [0, 0, 300])
        #expect(todayRows.map(\.id) == rows.map(\.id))        // ids survive a rescope
        #expect(UsageLedger.tokensText(0) == "—")
        #expect(UsageLedger.tokensText(800_000) == "800k")
    }

    @Test func sortsByEachColumnBothWays() {
        let rows = UsageLedger.rows(Self.ledgerSessions, scope: .all, today: Self.day("2026-07-12"))
        #expect(UsageLedger.sorted(rows, by: .lastActive, descending: true).map(\.session.uuid) == ["span", "yday", "old"])
        #expect(UsageLedger.sorted(rows, by: .lastActive, descending: false).map(\.session.uuid) == ["old", "yday", "span"])
        // Two "api" keys: disambiguated, then sorted as text (ties by last active).
        #expect(UsageLedger.sorted(rows, by: .project, descending: false).map(\.projectText)
                == ["api ~11111111", "api ~22222222", "web"])
        #expect(UsageLedger.sorted(rows, by: .project, descending: true).map(\.projectText)
                == ["web", "api ~22222222", "api ~11111111"])
        #expect(LedgerColumn.project.startsDescending == false)
        #expect(LedgerColumn.tokens.startsDescending)
        #expect(UsageLedger.header(.tokens, sortedBy: .tokens, descending: true, scope: .week) == "Tokens (7d) ▼")
        #expect(UsageLedger.header(.tokens, sortedBy: .project, descending: true, scope: .all) == "Tokens")
        #expect(UsageLedger.header(.project, sortedBy: .project, descending: false, scope: .all) == "Project ▲")
    }

    @Test func tiesKeepOneOrder() {
        let a = Self.session("a", first: "2026-07-12T10:00:00Z", last: "2026-07-12T11:00:00Z", days: [("2026-07-12", 5)])
        let b = Self.session("b", first: "2026-07-12T10:00:00Z", last: "2026-07-12T11:00:00Z", days: [("2026-07-12", 5)])
        let rows = UsageLedger.rows([b, a], scope: .all, today: Self.day("2026-07-12"))
        #expect(UsageLedger.sorted(rows, by: .tokens, descending: true).map(\.id) == ["r|a", "r|b"])
        #expect(UsageLedger.sorted(rows.reversed(), by: .tokens, descending: false).map(\.id) == ["r|a", "r|b"])
    }

    @Test func badgeShortModelsAndRelativeTimes() {
        #expect(UsageLedger.badge(["claude-fable-5": 600, "claude-sonnet-5-20260101": 300, "claude-opus-4": 100])
                == "fable-5 60% · sonnet 30%")
        #expect(UsageLedger.badge([:]) == "")
        #expect(UsageLedger.shortModel("claude-haiku-4-5") == "haiku")
        #expect(UsageLedger.shortModel("claude-fable-5-20260301") == "fable-5")
        #expect(UsageLedger.shortModel("<synthetic>") == "<synthetic>")
        let now = VaultTime.parse("2026-07-12T18:00:00Z")!
        #expect(UsageLedger.relative(now.addingTimeInterval(-60), now: now) == "just now")
        #expect(UsageLedger.relative(now.addingTimeInterval(-600), now: now) == "10m ago")
        #expect(UsageLedger.relative(now.addingTimeInterval(-3 * 3600), now: now) == "3h ago")
        #expect(UsageLedger.relative(now.addingTimeInterval(-5 * 86_400), now: now) == "5d ago")
        #expect(UsageLedger.relative(VaultTime.parse("2026-06-02T18:00:00Z")!, now: now, calendar: Self.cst) == "Jun 2")
    }

    @Test func rowDetailShowsSpanDaysModelsSkillsAndThePathOnlyWhenKept() {
        var s = Self.session("u1", first: "2026-07-10T10:00:00Z", last: "2026-07-11T12:30:00Z",
                             days: [("2026-07-11", 2_000), ("2026-07-10", 1_000)])
        s = VaultSessionInfo(uuid: s.uuid, root: s.root, projectKey: s.projectKey, projectName: s.projectName, cwd: nil,
                             firstTs: s.firstTs, lastTs: s.lastTs, total: s.total, byModel: s.byModel,
                             bySkill: ["review": 1_500], byDay: s.byDay, cache: nil, agentCount: 2, agentTokens: 700)
        let lines = UsageLedger.detail(s, recordTitle: ".claude-work")
        #expect(lines == [
            "Span (wall clock): 26h 30m  ·  Lifetime: 3.0k tokens",
            "Agents: 2 · 700 tokens",
            "2026-07-10: 1.0k  (fable-5 1.0k)",
            "2026-07-11: 2.0k  (fable-5 2.0k)",
            "Skills: review 1.5k",
            "Record: .claude-work  ·  Session: u1",
        ])
        let full = Self.session("u2", first: "2026-07-10T10:00:00Z", last: "2026-07-10T10:00:20Z",
                                days: [("2026-07-10", 5)], cwd: "/Users/x/Projects/api")
        let fullLines = UsageLedger.detail(full, recordTitle: ".claude")
        #expect(fullLines.first == "Span (wall clock): 1m  ·  Lifetime: 5 tokens")
        #expect(fullLines.contains("Folder: /Users/x/Projects/api"))
    }

    /// The export is the ledger port (Windows `VaultLedgerCsv`) fed the rows as shown: every
    /// session, the scoped and lifetime tokens, the shown project text, the record's title.
    @Test func csvMatchesTheLedgerPortInTheShownOrder() {
        let rows = UsageLedger.sorted(UsageLedger.rows(Self.ledgerSessions, scope: .yesterday, today: Self.day("2026-07-12")),
                                      by: .tokens, descending: true)
        let csv = UsageLedger.csv(rows, recordTitle: ".claude-work")
        #expect(csv.rowCount == 3)
        #expect(csv.text == VaultLedgerCsv.header
                + "yday,.claude-work,api ~22222222,2026-07-11T10:00:00Z,2026-07-11T11:00:00Z,800000,800000,claude-fable-5:800000\r\n"
                + "span,.claude-work,web,2026-07-10T10:00:00Z,2026-07-12T09:00:00Z,200,600,claude-fable-5:600\r\n"
                + "old,.claude-work,api ~11111111,2026-07-01T10:00:00Z,2026-07-01T12:00:00Z,0,900000,claude-fable-5:900000\r\n")
        let direct = VaultLedgerCsv.build(rows.map {
            VaultLedgerCsv.row($0.session, root: ".claude-work", project: $0.projectText, tokensInScope: $0.scoped)
        })
        #expect(csv.text == direct.text)
        #expect(UsageLedger.csv([], recordTitle: "x").text == VaultLedgerCsv.header)
        #expect(UsageLedger.exportName(Self.day("2026-07-02")) == "sanduhr-sessions-20260702.csv")
        #expect(UsagePageLoader.recordTitle(folder: "/Users/x/.claude-work") == ".claude-work")
    }

    @Test func datesReadInFull() {
        #expect(UsageDates.long(Self.day("2026-10-04")) == "October 4, 2026")
        #expect(UsageDates.short(Self.day("2026-09-30")) == "Sep 30")
    }

    // MARK: Large record

    /// 6,000 sessions over six months: read, rows, sort, a chip click and the CSV stay quick.
    @Test func aLargeRecordStaysQuick() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let id = "0123456789abcdef"
        let months = ["2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07"]
        var n = 0
        for month in months {
            var shard = VaultSessionShard(schemaVersion: 1, writerVersion: "test")
            for i in 0..<1_000 {
                let day = String(format: "%@-%02d", month, i % 28 + 1)
                var row = VaultSessionRow()
                row.projectKey = "p\(i % 40)~\(String(format: "%08x", i % 40))"
                row.projectName = "p\(i % 40)"
                row.firstTs = "\(day)T10:00:00.000000+00:00"
                row.lastTs = "\(day)T11:00:00.000000+00:00"
                row.byDay[day] = VaultDayBucket(total: Int64(1_000 + i), byModel: ["claude-fable-5": Int64(1_000 + i)])
                VaultRowMath.recomputeRowAggregates(&row)
                shard.sessions[String(format: "s-%06d", n)] = row
                n += 1
            }
            try b.store.saveSessionShard(id, month, shard)
        }
        let reader = VaultReader(store: b.store)
        let today = Self.day("2026-07-28")
        let clock = ContinuousClock()
        var rows: [LedgerRow] = []
        let read = clock.measure {
            rows = UsageLedger.rows(UsagePageLoader.sessions(recordID: id, vault: reader), scope: .week, today: today)
        }
        var sorted: [LedgerRow] = []
        let sort = clock.measure { sorted = UsageLedger.sorted(rows, by: .tokens, descending: true) }
        let chip = clock.measure {
            sorted = UsageLedger.sorted(UsageLedger.rescoped(sorted, scope: .all, today: today), by: .project, descending: false)
        }
        var csvRows = 0
        let csv = clock.measure { csvRows = UsageLedger.csv(sorted, recordTitle: ".claude").rowCount }
        #expect(rows.count == 6_000)
        #expect(csvRows == 6_000)
        print("usage-page large record: read+rows \(read), sort \(sort), chip \(chip), csv \(csv)")
        #expect(read + sort + chip + csv < .seconds(10))
    }
}
