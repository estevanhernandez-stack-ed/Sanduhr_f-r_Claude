import Foundation
import Testing
@testable import Sanduhr

/// The Codex usage service (item 3) over temp homes, with a fake ticker, an injected clock and an
/// inline (or held) runner, so no test races a timer: the Windows service's
/// `After_reset_a_callback_does_no_io` raced its own; here every tick and read is driven by hand.

/// Runs the read and its answer at once.
struct InlineCodexRunner: CodexReadRunner {
    func run(_ work: @escaping () -> CodexPollOutput, then done: @escaping (CodexPollOutput) -> Void) {
        done(work())
    }
}

/// Holds each read until the test lets it go.
final class HeldCodexRunner: CodexReadRunner {
    var held: [() -> Void] = []
    func run(_ work: @escaping () -> CodexPollOutput, then done: @escaping (CodexPollOutput) -> Void) {
        held.append { done(work()) }
    }
}

@MainActor
@Suite("Codex usage service")
struct CodexUsageServiceTests {
    let temp = CodexTempDir()
    var home: URL { temp.url.appendingPathComponent("codex", isDirectory: true) }
    let now = CodexFixture.date("2026-11-09T12:00:00Z")
    var asOf: Date { now.addingTimeInterval(-600) }

    final class Box<T> { var value: T; init(_ v: T) { value = v } }

    private func rollout(_ lines: String...) throws {
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        try CodexFixture.rollout(home, day: "09", name: "rollout-2026-11-09T11-00-00-test",
                                 body: lines.joined(separator: "\n") + "\n")
    }

    private func event(at: Date, five: Double, week: Double, fiveResets: Date? = nil, weekResets: Date? = nil,
                       weekMinutes: Int = 10080) -> String {
        let r5 = Int64((fiveResets ?? now.addingTimeInterval(3 * 3600)).timeIntervalSince1970)
        let r7 = Int64((weekResets ?? now.addingTimeInterval(5 * 86400)).timeIntervalSince1970)
        return #"{"timestamp":"\#(ISO8601.stamp(at))","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":\#(five),"window_minutes":300,"resets_at":\#(r5)},"secondary":{"used_percent":\#(week),"window_minutes":\#(weekMinutes),"resets_at":\#(r7)},"plan_type":"plus"}}}"#
    }

    private func make(installed: Bool = true, ticker: FakeTicker = FakeTicker(), clock: Box<Date>? = nil,
                      runner: CodexReadRunner = InlineCodexRunner(), spy: SpyCodexFS = SpyCodexFS(),
                      homeCalls: Box<Int> = Box(0), logs: Box<[String]> = Box([]),
                      reset: Box<Bool> = Box(false)) -> CodexUsageService {
        let home = self.home
        let clock = clock ?? Box(now)
        return CodexUsageService(
            homes: { homeCalls.value += 1; return installed ? [home] : [] },
            fileSystem: spy, ticker: ticker, clock: { clock.value }, runner: runner,
            resetDone: { reset.value }, log: { logs.value.append($0) })
    }

    // MARK: When it runs

    /// A Mac with only Claude on (no setup, or Claude alone ticked) never polls Codex: 2.45.0's behavior.
    @Test func claudeOnlyNeverPolls() {
        #expect(!CodexUsageService.wants(setup: nil, sources: .default, demo: false))
        let claudeOnly = SetupSettings(desktop: true, usage: [SetupSettings.claude: true])
        #expect(!CodexUsageService.wants(setup: claudeOnly, sources: .default, demo: false))
        let both = SetupSettings(desktop: true, usage: [SetupSettings.claude: true, SetupSettings.codex: true])
        #expect(CodexUsageService.wants(setup: both, sources: .default, demo: false))
    }

    @Test func demoModeNeverReadsCodex() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let both = SetupSettings(desktop: true, usage: [SetupSettings.claude: true, SetupSettings.codex: true])
        let on = CodexUsageService.wants(setup: both, sources: .default, demo: true)
        #expect(!on)
        let spy = SpyCodexFS(), ticker = FakeTicker(), calls = Box(0)
        let svc = make(ticker: ticker, spy: spy, homeCalls: calls)
        svc.update(on: on)
        #expect(!ticker.running)
        #expect(calls.value == 0)
        #expect(!spy.touchedAnything)
        #expect(svc.codexUsage == nil && svc.emptyState == nil)
    }

    @Test func offDoesNoIOAndStartsNoTimer() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let spy = SpyCodexFS(), ticker = FakeTicker(), calls = Box(0)
        let svc = make(ticker: ticker, spy: spy, homeCalls: calls)
        svc.update(on: false)
        #expect(!ticker.running && ticker.starts == 0)
        #expect(calls.value == 0 && !spy.touchedAnything)
    }

    @Test func onPollsAtOnceAndEveryThirtySeconds() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let ticker = FakeTicker(), calls = Box(0)
        let svc = make(ticker: ticker, homeCalls: calls)
        svc.update(on: true)
        #expect(ticker.running && ticker.interval == 30)
        #expect(calls.value == 1)
        svc.update(on: true)
        #expect(ticker.starts == 1 && calls.value == 1)
        ticker.tick?()
        #expect(calls.value == 2)
    }

    // MARK: What it publishes

    @Test func aReadingPublishesTheTiersAsOfAndFresh() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let svc = make()
        svc.update(on: true)
        #expect(svc.codexUsage?.tiers[.fiveHour]?.utilization == 40)
        #expect(svc.codexUsage?.tiers[.sevenDay]?.utilization == 10)
        #expect(svc.asOf == ISO8601.parse(ISO8601.stamp(asOf)))
        #expect(svc.freshness == .fresh)
        #expect(svc.freshTiers == [.fiveHour, .sevenDay])
        #expect(svc.planType == "plus")
        #expect(svc.emptyState == nil)
    }

    @Test func aLimitGoesStaleThirtyMinutesAfterItsAsOf() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let ticker = FakeTicker(), clock = Box(now)
        let svc = make(ticker: ticker, clock: clock)
        svc.update(on: true)
        #expect(svc.freshness == .fresh)
        clock.value = asOf.addingTimeInterval(31 * 60)
        ticker.tick?()
        #expect(svc.freshness == .stale)
        #expect(svc.freshTiers.isEmpty)
        // The last known numbers stay, dimmed by the view.
        #expect(svc.codexUsage?.tiers[.fiveHour]?.utilization == 40)
    }

    @Test func aPassedResetReadsZeroAndFresh() throws {
        try rollout(event(at: asOf.addingTimeInterval(-3600), five: 99, week: 99,
                          fiveResets: now.addingTimeInterval(-300), weekResets: now.addingTimeInterval(-300)))
        let svc = make()
        svc.update(on: true)
        #expect(svc.codexUsage?.tiers[.fiveHour]?.utilization == 0)
        #expect(svc.codexUsage?.tiers[.sevenDay]?.utilization == 0)
        #expect(svc.freshness == .fresh)
    }

    @Test func anIdleCodexIsFreshOnlyWhereItsResetPassed() throws {
        try rollout(event(at: asOf, five: 99, week: 85, fiveResets: asOf.addingTimeInterval(5 * 3600),
                          weekResets: now.addingTimeInterval(3 * 86400)))
        let svc = make(clock: Box(asOf.addingTimeInterval(6 * 3600)))
        svc.update(on: true)
        #expect(svc.freshness == .fresh)
        #expect(svc.freshTiers == [.fiveHour])
        #expect(svc.codexUsage?.tiers[.sevenDay]?.utilization == 85)
    }

    @Test func anOddWindowIsDroppedAndLoggedOnceWithNoPath() throws {
        try rollout(event(at: asOf, five: 40, week: 10, weekMinutes: 1440))
        let ticker = FakeTicker(), logs = Box<[String]>([])
        let svc = make(ticker: ticker, logs: logs)
        svc.update(on: true)
        ticker.tick?()
        ticker.tick?()
        #expect(svc.codexUsage?.tiers.keys.map(\.rawValue) == ["five_hour"])
        #expect(logs.value == ["codex: unknown window of 1440 minutes ignored"])
    }

    @Test func notInstalled() {
        let svc = make(installed: false)
        svc.update(on: true)
        #expect(svc.emptyState == .notInstalled)
        #expect(svc.codexUsage == nil)
    }

    @Test func installedWithNoLimitsYet() throws {
        try rollout(CodexFixture.premium)
        let svc = make()
        svc.update(on: true)
        #expect(svc.emptyState == .noLimitsYet)
        #expect(svc.codexUsage == nil && svc.freshness == nil)
    }

    @Test func couldNotReadLogsNoPath() throws {
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        try CodexFixture.rollout(home, day: "09", name: "rollout-bad", body: "", zst: Data("garbage".utf8))
        let logs = Box<[String]>([])
        let svc = make(logs: logs)
        svc.update(on: true)
        #expect(svc.emptyState == .couldNotRead)
        #expect(!logs.value.isEmpty)
        #expect(logs.value.allSatisfy { !$0.contains("/") && !$0.contains("rollout-bad") })
    }

    // MARK: Stopping

    @Test func offClearsWhatItPublished() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let ticker = FakeTicker()
        let svc = make(ticker: ticker)
        svc.update(on: true)
        #expect(svc.codexUsage != nil)
        svc.update(on: false)
        #expect(!ticker.running && !svc.running)
        #expect(svc.codexUsage == nil && svc.asOf == nil && svc.freshness == nil && svc.emptyState == nil)
    }

    @Test func sparklinesAreHeldForTheWidgetAndClearedOnStop() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let svc = make()
        svc.update(on: true)
        let h: HistoryStore.History = [Tier.fiveHour.rawValue: [HistoryStore.Point(t: ISO8601.stamp(asOf), v: 40)]]
        svc.setSparklines(h)
        #expect(svc.sparklines == h)
        svc.update(on: false)
        #expect(svc.sparklines.isEmpty)
    }

    @Test func aTickAfterStopDoesNoIO() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let ticker = FakeTicker(), spy = SpyCodexFS(), calls = Box(0)
        let svc = make(ticker: ticker, spy: spy, homeCalls: calls)
        svc.update(on: true)
        svc.update(on: false)
        let opened = spy.opened.count, homes = calls.value
        ticker.tick?()   // a tick the timer had already queued
        svc.poll()
        #expect(spy.opened.count == opened && calls.value == homes)
        #expect(svc.codexUsage == nil)
    }

    @Test func aReadQueuedBeforeStopDoesNoIOAndPublishesNothing() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let runner = HeldCodexRunner(), spy = SpyCodexFS(), calls = Box(0)
        let svc = make(runner: runner, spy: spy, homeCalls: calls)
        svc.update(on: true)
        #expect(runner.held.count == 1)
        svc.update(on: false)
        runner.held.removeFirst()()
        #expect(calls.value == 0 && !spy.touchedAnything)
        #expect(svc.codexUsage == nil && svc.emptyState == nil)
    }

    @Test func aReadInFlightSkipsTheNextTick() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let runner = HeldCodexRunner(), ticker = FakeTicker()
        let svc = make(ticker: ticker, runner: runner)
        svc.update(on: true)
        ticker.tick?()
        #expect(runner.held.count == 1)
        runner.held.removeFirst()()
        #expect(svc.codexUsage?.tiers[.fiveHour]?.utilization == 40)
        ticker.tick?()
        #expect(runner.held.count == 1)
    }

    @Test func afterResetNothingReads() throws {
        try rollout(event(at: asOf, five: 40, week: 10))
        let ticker = FakeTicker(), calls = Box(0), reset = Box(false)
        let svc = make(ticker: ticker, homeCalls: calls, reset: reset)
        svc.update(on: true)
        reset.value = true
        ticker.tick?()
        #expect(calls.value == 1)
    }
}
