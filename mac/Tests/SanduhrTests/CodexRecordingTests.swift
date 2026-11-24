import Foundation
import Testing
@testable import Sanduhr

/// Item 4: Codex's history, snapshot and alerts, per source. The history file name, the snapshot's
/// shape and removal, Claude's once-keys and texts pinned unchanged, a Codex alert on a fresh limit
/// and none on a stale one, the switch, and the service's publish hook. Temp folders, in-memory
/// defaults and made-up numbers only; nothing reads the real Application Support or `~/.codex`.

private func tempDir() throws -> URL {
    let dir = FileManager.default.temporaryDirectory
        .appendingPathComponent("sanduhr-codex-rec-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    return dir
}

private func json(_ url: URL) -> [String: Any]? {
    (try? Data(contentsOf: url)).flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
}

private func exists(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: url.path) }

private let asOf = parseISO("2026-11-09T11:50:00Z")!
private let reset5 = "2026-11-09T15:00:00Z"
private let reset7 = "2026-11-14T12:00:00Z"

private func usage(five: Double? = 40, week: Double? = 12) -> UsageResponse {
    var u = UsageResponse()
    if let five { u.tiers[.fiveHour] = TierUsage(utilization: five, resetsAt: reset5) }
    if let week { u.tiers[.sevenDay] = TierUsage(utilization: week, resetsAt: reset7) }
    return u
}

private func publication(five: Double? = 40, week: Double? = 12, at: Date = asOf,
                         fresh: Set<Tier> = [.fiveHour, .sevenDay], plan: String? = "plus") -> CodexPublication {
    CodexPublication(usage: usage(five: five, week: week), asOf: at, freshTiers: fresh, planType: plan)
}

// MARK: - The snapshot file

@Suite("Codex snapshot file")
struct CodexSnapshotFileTests {
    @Test func theFileIsBesideSnapshotJsonAndNamedForCodex() {
        #expect(SnapshotWriter.codexURL.lastPathComponent == "snapshot.codex.json")
        #expect(SnapshotWriter.codexURL.deletingLastPathComponent() == SnapshotWriter.url.deletingLastPathComponent())
    }

    @Test func theShapeIsSnapshotJsonsPlusASourceAndNoAccountRef() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("snapshot.codex.json")
        SnapshotWriter.writeOk(usage(five: 40.6, week: 12), accountRef: nil, plan: "plus", now: asOf,
                               source: "codex", to: url)
        let root = try #require(json(url))
        #expect(Set(root.keys) == ["schema_version", "writer_version", "captured_at", "plan", "source", "status", "error_kind", "tiers"])
        #expect(root["source"] as? String == "codex")
        #expect(root["account_ref"] == nil)
        #expect(root["schema_version"] as? Int == 1)
        #expect(root["status"] as? String == "ok")
        #expect(root["plan"] as? String == "plus")
        #expect(root["captured_at"] as? String == ISO8601.stamp(asOf))
        #expect(root["error_kind"] is NSNull)
        let tiers = try #require(root["tiers"] as? [[String: Any]])
        #expect(tiers.map { $0["key"] as? String } == ["five_hour", "seven_day"])
        // Utilization is an Int, as Windows writes it; the reset time is the ISO string.
        #expect(tiers[0]["utilization"] as? Int == 40)
        #expect(tiers[0]["resets_at"] as? String == reset5)
        #expect(tiers[1]["utilization"] as? Int == 12)
        #expect(tiers[0]["used"] is NSNull && tiers[0]["limit"] is NSNull)
    }

    /// Claude's snapshot keeps its `account_ref` and gains no `source` key: snapshot.json is unchanged.
    @Test func claudesSnapshotIsUnchanged() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("snapshot.json")
        SnapshotWriter.writeOk(usage(), accountRef: "0a1b2c3d", plan: "Max", now: asOf, to: url)
        let root = try #require(json(url))
        #expect(root["account_ref"] as? String == "0a1b2c3d")
        #expect(root["source"] == nil)
    }

    /// Seat sync lists `snapshot.<8 hex>.json` only: it neither reads nor deletes Codex's file.
    @Test func seatSyncNeitherSyncsNorDeletesTheCodexSnapshot() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        #expect(SeatSnapshots.ref(inFileName: "snapshot.codex.json") == nil)
        let codex = dir.appendingPathComponent("snapshot.codex.json")
        SnapshotWriter.writeOk(usage(), accountRef: nil, plan: nil, now: asOf, source: "codex", to: codex)
        let files = SeatSnapshots.Files(dir: dir)
        #expect(files.snapshotRefs().isEmpty)
        // No seats at all: every stale per-account file goes, and Codex's stays.
        let stale = dir.appendingPathComponent("snapshot.0a1b2c3d.json")
        SnapshotWriter.writeOk(usage(), accountRef: "0a1b2c3d", now: asOf, to: stale)
        SeatSnapshots.sync([], in: files, now: asOf)
        #expect(!exists(stale))
        #expect(exists(codex))
    }
}

// MARK: - The recorder: history, snapshot, alerts

@MainActor
@Suite("Codex recorder")
struct CodexRecorderTests {
    final class Spy {
        var alerts: [(UsageResponse, Set<Tier>)] = []
        var forgot = 0
    }

    let dir: URL
    let defaults = MemoryDefaults()
    let spy = Spy()
    var historyFiles: HistoryStore.Files { HistoryStore.Files(dir: dir) }
    var snapshotURL: URL { dir.appendingPathComponent("snapshot.codex.json") }
    var historyURL: URL { dir.appendingPathComponent("history.@codex.json") }

    init() throws { dir = try tempDir() }

    func make(now: @escaping () -> Date = { asOf.addingTimeInterval(60) }) -> CodexRecorder {
        let spy = self.spy
        return CodexRecorder(files: historyFiles, defaults: defaults, snapshot: snapshotURL, clock: now,
                             alert: { spy.alerts.append(($0, $1)) }, forget: { spy.forgot += 1 })
    }

    /// The history queue drains before a read: writes run on it.
    func drain() { HistoryStore.io.sync {} }

    func series(_ tier: Tier) -> [HistoryStore.Point] {
        drain()
        return historyFiles.load("@codex")[tier.rawValue] ?? []
    }

    // MARK: History

    @Test func readingsGoToTheReservedCodexHistoryFile() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        #expect(historyFiles.url(for: CodexRecorder.historyAccount).lastPathComponent == "history.@codex.json")
        // A Claude account label can't start with "@", so the two never collide.
        #expect(!AccountRegistry.isValid("@codex"))
        let rec = make()
        rec.record(publication(five: 40.4, week: 12.6))
        #expect(series(.fiveHour).map(\.v) == [40])
        #expect(series(.sevenDay).map(\.v) == [13])
        #expect(exists(historyURL))
        // Claude's own file is not touched.
        #expect(!exists(dir.appendingPathComponent("history.json")))
    }

    @Test func theSameEventIsNotRecordedTwice() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        rec.record(publication())
        rec.record(publication())
        #expect(series(.fiveHour).count == 1)
    }

    /// A restart replays the newest event: the file's newest point is newer than it, so it is skipped.
    @Test func aRestartDoesNotRecordTheSameEventAgain() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        make().record(publication())
        drain()
        make().record(publication())
        #expect(series(.fiveHour).count == 1)
    }

    @Test func anUnchangedNumberWithinFiveMinutesIsSkippedAndAChangeIsKept() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        rec.record(publication(five: 40, week: 12))
        rec.record(publication(five: 40, week: 13, at: asOf.addingTimeInterval(120)))
        #expect(series(.fiveHour).map(\.v) == [40])
        #expect(series(.sevenDay).map(\.v) == [12, 13])
        // Same number again, but five minutes on.
        rec.record(publication(five: 40, week: 13, at: asOf.addingTimeInterval(420)))
        #expect(series(.fiveHour).map(\.v) == [40, 40])
    }

    @Test func meterHistoryOffForCodexRecordsNothing() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        MeterHistory.set(false, for: CodexRecorder.historyAccount, in: defaults)
        make().record(publication())
        drain()
        #expect(!exists(historyURL))
        // The snapshot is not history: it is still written.
        #expect(exists(snapshotURL))
    }

    // MARK: Sparklines

    @Test func theRecorderHandsTheWidgetItsSparklinePoints() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        var seen: [HistoryStore.History] = []
        let rec = make()
        rec.onSparklines = { seen.append($0) }
        rec.record(publication(five: 40, week: 12))
        rec.record(publication(five: 45, week: 12, at: asOf.addingTimeInterval(120)))
        #expect(seen.last?[Tier.fiveHour.rawValue]?.map(\.v) == [40, 45])
        #expect(seen.last?[Tier.sevenDay.rawValue]?.map(\.v) == [12])
    }

    @Test func aRestartSeedsTheSparklinesFromTheFile() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        make().record(publication(five: 40, week: 12))
        drain()
        var seen: [HistoryStore.History] = []
        let again = make()
        again.onSparklines = { seen.append($0) }
        // The same event replays: nothing new is recorded, but the widget still gets what is on file.
        again.record(publication(five: 40, week: 12))
        #expect(seen.last?[Tier.fiveHour.rawValue]?.map(\.v) == [40])
    }

    @Test func meterHistoryOffForCodexHandsTheWidgetNothing() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        MeterHistory.set(false, for: CodexRecorder.historyAccount, in: defaults)
        var calls = 0
        let rec = make()
        rec.onSparklines = { _ in calls += 1 }
        rec.record(publication())
        #expect(calls == 0)
    }

    // MARK: Snapshot

    @Test func theSnapshotIsWrittenStampedWithTheEventsTime() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        make().record(publication(plan: "pro"))
        let root = try #require(json(snapshotURL))
        #expect(root["source"] as? String == "codex")
        #expect(root["captured_at"] as? String == ISO8601.stamp(asOf))
        #expect(root["plan"] as? String == "pro")
    }

    @Test func noReadingRemovesTheSnapshot() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        rec.record(publication())
        #expect(exists(snapshotURL))
        rec.record(nil)
        #expect(!exists(snapshotURL))
        // Back with a reading, the file is written again.
        rec.record(publication())
        #expect(exists(snapshotURL))
    }

    @Test func turningCodexOffRemovesTheSnapshotAndForgetsTheBaseline() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        rec.record(publication())
        rec.stopped(codexOn: false)
        #expect(!exists(snapshotURL))
        #expect(spy.forgot == 1)
    }

    /// Demo mode stops the service but leaves Codex ticked: the file is a true reading, so it stays.
    @Test func stoppingWhileCodexIsStillOnLeavesTheSnapshot() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        rec.record(publication())
        rec.stopped(codexOn: true)
        #expect(exists(snapshotURL))
        #expect(spy.forgot == 1)
    }

    @Test func nothingIsWrittenAfterReset() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        ResetGate.$testOverride.withValue(true) {
            rec.record(publication())
        }
        drain()
        #expect(!exists(snapshotURL) && !exists(historyURL))
    }

    // MARK: Alerts

    @Test func eachNewStateReachesTheAlertsWithItsFreshTiers() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        let rec = make()
        rec.record(publication(fresh: [.fiveHour]))
        #expect(spy.alerts.count == 1)
        #expect(spy.alerts[0].1 == [.fiveHour])
        #expect(spy.alerts[0].0.tiers[.sevenDay]?.utilization == 12)
        // Polled again with nothing new: not evaluated again.
        rec.record(publication(fresh: [.fiveHour]))
        #expect(spy.alerts.count == 1)
        // A tier turning fresh (its reset passed) is a new state.
        rec.record(publication(fresh: [.fiveHour, .sevenDay]))
        #expect(spy.alerts.count == 2)
    }

    @Test func noReadingDoesNotAlert() throws {
        defer { try? FileManager.default.removeItem(at: dir) }
        make().record(nil)
        #expect(spy.alerts.isEmpty)
    }
}

// MARK: - Alert rules, per source

@Suite("Alert rules per source")
struct SourceAlertRulesTests {
    let now = parseISO("2026-11-09T12:00:00Z")!

    var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.locale = Locale(identifier: "en_US_POSIX")
        return c
    }

    func settings(_ change: (inout AlertSettings) -> Void = { _ in }) -> AlertSettings {
        var s = AlertSettings()
        s.enabled = true
        change(&s)
        return s
    }

    func run(_ u: UsageResponse, previous: UsageResponse? = nil, _ s: AlertSettings, source: AgentSource = .claude,
             fresh: Set<Tier>? = nil, fired: Set<String> = [], desk: Bool = false) -> AlertOutcome {
        AlertRules.evaluate(usage: u, previous: previous, settings: s, fired: fired, deskRunning: desk,
                            now: now, source: source, freshTiers: fresh, calendar: calendar)
    }

    /// Claude's keys and texts, exactly as 2.45.0 wrote them.
    @Test func claudesOnceKeysAndTextsAreUnchanged() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 100, resetsAt: "2026-11-09T15:00:00Z")
        u.tiers[.sevenDay] = TierUsage(utilization: 85, resetsAt: "2026-11-14T12:00:00Z")
        let out = run(u, settings { $0.sessionFull = true })
        #expect(out.alerts.map(\.onceKey) == [
            "five_hour|2026-11-09T15:00:00Z|line", "five_hour|2026-11-09T15:00:00Z|full",
            "seven_day|2026-11-14T12:00:00Z|line"])
        #expect(out.alerts.map(\.title) == ["Session at 100%", "Session limit reached", "Weekly — All Models at 85%"])
        #expect(out.alerts.map(\.body) == ["Resets 3:00 PM.", "Resets 3:00 PM.", "Resets Sat 12:00 PM."])
        // Naming the source Claude is the same as naming none.
        #expect(run(u, settings { $0.sessionFull = true }, source: .claude, fresh: nil) == out)
    }

    @Test func claudesResetAlertsAreUnchanged() {
        var before = UsageResponse(), after = UsageResponse()
        before.tiers[.fiveHour] = TierUsage(utilization: 90, resetsAt: "2026-11-09T11:00:00Z")
        before.tiers[.sevenDay] = TierUsage(utilization: 90, resetsAt: "2026-11-09T11:00:00Z")
        after.tiers[.fiveHour] = TierUsage(utilization: 2, resetsAt: "2026-11-09T17:00:00Z")
        after.tiers[.sevenDay] = TierUsage(utilization: 2, resetsAt: "2026-11-16T12:00:00Z")
        let out = run(after, previous: before, settings { $0.sessionReset = true; $0.weeklyReset = true })
        #expect(out.alerts.map(\.onceKey) == ["five_hour|2026-11-09T11:00:00Z|reset", "seven_day|2026-11-09T11:00:00Z|reset"])
        #expect(out.alerts.map(\.title) == ["Session reset", "Weekly — All Models reset"])
    }

    @Test func aCodexAlertFiresOnAFreshLimitWithItsOwnKeyAndText() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 88, resetsAt: "2026-11-09T15:00:00Z")
        u.tiers[.sevenDay] = TierUsage(utilization: 91, resetsAt: "2026-11-14T12:00:00Z")
        let out = run(u, settings { $0.codexAlerts = true }, source: .codex, fresh: [.fiveHour, .sevenDay])
        #expect(out.alerts.map(\.onceKey) == ["codex|five_hour|2026-11-09T15:00:00Z|line",
                                              "codex|seven_day|2026-11-14T12:00:00Z|line"])
        #expect(out.alerts.map(\.title) == ["Codex 5-hour at 88%", "Codex weekly at 91%"])
        #expect(out.alerts.map(\.body) == ["Resets 3:00 PM.", "Resets Sat 12:00 PM."])
    }

    @Test func aStaleLimitNeverAlerts() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 88, resetsAt: "2026-11-09T15:00:00Z")
        u.tiers[.sevenDay] = TierUsage(utilization: 91, resetsAt: "2026-11-14T12:00:00Z")
        let s = settings { $0.codexAlerts = true }
        #expect(run(u, s, source: .codex, fresh: []).alerts.isEmpty)
        // Per tier: only the fresh one speaks.
        let one = run(u, s, source: .codex, fresh: [.sevenDay])
        #expect(one.alerts.map(\.title) == ["Codex weekly at 91%"])
    }

    @Test func theCodexSwitchIsOffByDefaultAndGatesCodexOnly() {
        #expect(AlertSettings().codexAlerts == false)
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 95, resetsAt: "2026-11-09T15:00:00Z")
        #expect(run(u, settings(), source: .codex, fresh: [.fiveHour]).alerts.isEmpty)
        // Claude's alerts do not depend on it.
        #expect(run(u, settings()).alerts.count == 1)
        // And the master switch still rules.
        #expect(run(u, settings { $0.enabled = false; $0.codexAlerts = true }, source: .codex, fresh: [.fiveHour]).alerts.isEmpty)
    }

    @Test func theSwitchReadsFromItsOwnDefaultsKey() {
        let d = MemoryDefaults()
        #expect(AlertSettings(d).codexAlerts == false)
        d.set(true, forKey: Notifier.Key.codexAlerts)
        #expect(AlertSettings(d).codexAlerts)
        #expect(Notifier.Key.codexAlerts != Notifier.Key.enabled)
    }

    @Test func claudeAndCodexKeepTheirOwnOncePerWindowMemory() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 95, resetsAt: "2026-11-09T15:00:00Z")
        let s = settings { $0.codexAlerts = true }
        let claude = run(u, s)
        let codex = run(u, s, source: .codex, fresh: [.fiveHour], fired: Set(claude.alerts.map(\.onceKey)))
        // Claude's fired key does not silence Codex's alert on the same window and tier.
        #expect(codex.alerts.count == 1)
        // And Codex's own key does silence a repeat.
        let again = run(u, s, source: .codex, fresh: [.fiveHour], fired: Set(codex.alerts.map(\.onceKey)))
        #expect(again.alerts.isEmpty)
    }

    @Test func codexFullPaceAndResetSayCodex() {
        var before = UsageResponse(), u = UsageResponse()
        before.tiers[.fiveHour] = TierUsage(utilization: 90, resetsAt: "2026-11-09T11:00:00Z")
        before.tiers[.sevenDay] = TierUsage(utilization: 90, resetsAt: "2026-11-09T11:00:00Z")
        u.tiers[.fiveHour] = TierUsage(utilization: 100, resetsAt: "2026-11-09T14:00:00Z")
        u.tiers[.sevenDay] = TierUsage(utilization: 0, resetsAt: "2026-11-16T12:00:00Z")
        let s = settings { $0.codexAlerts = true; $0.sessionFull = true; $0.weeklyReset = true; $0.sessionReset = true }
        let out = run(u, previous: before, s, source: .codex, fresh: [.fiveHour, .sevenDay])
        let titles = Set(out.alerts.map(\.title))
        #expect(titles.contains("Codex 5-hour limit reached"))
        #expect(titles.contains("Codex weekly reset"))
        #expect(out.alerts.allSatisfy { $0.onceKey.hasPrefix("codex|") })

        var paced = UsageResponse()
        paced.tiers[.fiveHour] = TierUsage(utilization: 80, resetsAt: "2026-11-09T14:00:00Z")
        let pace = run(paced, settings { $0.codexAlerts = true; $0.pace = true; $0.sessionLine = 95 },
                       source: .codex, fresh: [.fiveHour])
        #expect(pace.alerts.map(\.title).first?.hasPrefix("Codex 5-hour on pace to run out at ") == true)
    }

    /// A Codex alert pulses the Desk where a piece shows Codex (`desk` is the caller's "there is one", 2.48.0;
    /// DeskCodexRouteTests has the rest) and is a banner where none does. Claude's route is the same either way.
    @Test func aCodexAlertPulsesOnlyWhereTheDeskCanShowIt() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 95, resetsAt: "2026-11-09T15:00:00Z")
        let s = settings { $0.codexAlerts = true; $0.delivery = .desk }
        let codex = run(u, s, source: .codex, fresh: [.fiveHour], desk: true)
        #expect(!codex.banner && codex.pulse)
        let none = run(u, s, source: .codex, fresh: [.fiveHour], desk: false)
        #expect(none.banner && !none.pulse)
        let claude = run(u, s, desk: true)
        #expect(!claude.banner && claude.pulse)
    }
}

// MARK: - The service's publish hook

@MainActor
@Suite("Codex service publishes")
struct CodexServicePublishTests {
    let temp = CodexTempDir()
    var home: URL { temp.url.appendingPathComponent("codex", isDirectory: true) }
    let now = CodexFixture.date("2026-11-09T12:00:00Z")

    func rollout(five: Double, week: Double, at: Date) throws {
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        let r5 = Int64(now.addingTimeInterval(3 * 3600).timeIntervalSince1970)
        let r7 = Int64(now.addingTimeInterval(5 * 86400).timeIntervalSince1970)
        let line = #"{"timestamp":"\#(ISO8601.stamp(at))","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":\#(five),"window_minutes":300,"resets_at":\#(r5)},"secondary":{"used_percent":\#(week),"window_minutes":10080,"resets_at":\#(r7)},"plan_type":"plus"}}}"#
        try CodexFixture.rollout(home, day: "09", name: "rollout-2026-11-09T11-00-00-test", body: line + "\n")
    }

    func make(ticker: FakeTicker = FakeTicker()) -> CodexUsageService {
        let home = self.home, now = self.now
        return CodexUsageService(homes: { [home] }, fileSystem: SpyCodexFS(), ticker: ticker, clock: { now },
                                 runner: InlineCodexRunner(), resetDone: { false }, log: { _ in })
    }

    @Test func aReadingIsHandedOverWithItsFreshTiersAndPlan() throws {
        let asOf = now.addingTimeInterval(-600)
        try rollout(five: 40, week: 12, at: asOf)
        let svc = make()
        var got: [CodexPublication?] = []
        svc.onUpdate = { got.append($0) }
        svc.update(on: true)
        #expect(got.count == 1)
        let p = try #require(got[0])
        #expect(p.usage.tiers[.fiveHour]?.utilization == 40)
        #expect(p.usage.tiers[.sevenDay]?.utilization == 12)
        #expect(p.freshTiers == [.fiveHour, .sevenDay])
        #expect(p.planType == "plus")
        #expect(abs(p.asOf.timeIntervalSince(asOf)) < 1)
    }

    @Test func aMacWithNoLimitsHandsOverNil() throws {
        let svc = make()
        var got: [CodexPublication?] = []
        svc.onUpdate = { got.append($0) }
        svc.update(on: true)
        #expect(got.count == 1 && got[0] == nil)
    }

    @Test func stoppingTellsTheRecorderOnlyWhenItWasRunning() throws {
        try rollout(five: 40, week: 12, at: now.addingTimeInterval(-600))
        let svc = make()
        var stops = 0
        svc.onStop = { stops += 1 }
        svc.update(on: false)
        #expect(stops == 0)   // a Claude-only Mac: never started, nothing to tell
        svc.update(on: true)
        svc.update(on: false)
        #expect(stops == 1)
        svc.update(on: false)
        #expect(stops == 1)
    }
}
