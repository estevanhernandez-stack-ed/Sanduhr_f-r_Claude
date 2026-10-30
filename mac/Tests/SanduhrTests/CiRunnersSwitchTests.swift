import Foundation
import Testing
@testable import Sanduhr

/// Settings, Watchers, "Show my CI runners' jobs" (spec section 15): off by default, findable in
/// Settings search, and the watch runs only while the switch is on and the Desk shows. Ported from
/// Windows' CiRunnersSwitchTests; the Desk rule is the Mac's.
@MainActor
@Suite("CI runners switch")
struct CiRunnersSwitchTests {
    private final class Desk { var running = false }

    private final class Counting: DockerAPI, @unchecked Sendable {
        private let lock = NSLock()
        private var n = 0
        var lists: Int { lock.withLock { n } }
        func listContainers() async throws -> Data { lock.withLock { n += 1 }; return Data("[]".utf8) }
        func followLogs(id: String) async throws -> AsyncThrowingStream<Data, Error> { AsyncThrowingStream { $0.finish() } }
    }

    private final class Cleared: RunnerSink {
        var clears = 0
        func apply(key: String, runner: String, line: RunnerLine) {}
        func gone(key: String) {}
        func clear() { clears += 1 }
    }

    private func scratch() -> UserDefaults {
        let d = UserDefaults(suiteName: "sanduhr.tests.cirunners.\(UUID().uuidString)")!
        return d
    }

    private func settle() async throws { try await Task.sleep(for: .milliseconds(60)) }

    @Test func theSwitchIsOffByDefault() {
        let d = scratch()
        let desk = Desk()
        let c = RunnerWatchController(defaults: d, deskRunning: { desk.running }, docker: Counting(), sink: Cleared())
        #expect(!c.isOn)
        #expect(!d.bool(forKey: RunnerWatchController.switchKey))
        #expect(RunnerWatchController.switchKey == "watchersCiRunners")
        #expect(SettingsNames.ciRunnersSwitch == "Show my CI runners' jobs") // Windows' name
        c.refresh()
        #expect(c.status == .off)
    }

    @Test func nothingRunsWhileTheSwitchIsOff() async throws {
        let docker = Counting()
        let desk = Desk()
        desk.running = true
        let c = RunnerWatchController(defaults: scratch(), deskRunning: { desk.running }, docker: docker, sink: Cleared())
        c.refresh()
        try await settle()
        #expect(docker.lists == 0)
    }

    @Test func nothingRunsWhileTheDeskIsOff() async throws {
        let docker = Counting()
        let d = scratch()
        d.set(true, forKey: RunnerWatchController.switchKey)
        let desk = Desk()
        let c = RunnerWatchController(defaults: d, deskRunning: { desk.running }, docker: docker, sink: Cleared())
        c.refresh()
        try await settle()
        #expect(docker.lists == 0)
        #expect(c.status == .off)
        #expect(c.statusLine == "Waits for the Desk to be on.")
    }

    @Test func itRunsWhileBothAreOnAndStopsCleanlyWhenEitherTurnsOff() async throws {
        let docker = Counting()
        let d = scratch()
        d.set(true, forKey: RunnerWatchController.switchKey)
        let desk = Desk()
        desk.running = true
        let sink = Cleared()
        let c = RunnerWatchController(defaults: d, deskRunning: { desk.running }, docker: docker, sink: sink)
        c.refresh()
        c.refresh() // idempotent: one loop
        for _ in 0..<200 where docker.lists == 0 { try await Task.sleep(for: .milliseconds(10)) }
        #expect(docker.lists == 1)
        #expect(c.status.kind == .noRunners)
        #expect(c.statusLine == "No runner containers are running.")

        desk.running = false // the Desk stopped
        c.refresh()
        #expect(c.status == .off)
        #expect(sink.clears == 1)

        desk.running = true
        c.refresh()
        for _ in 0..<200 where docker.lists < 2 { try await Task.sleep(for: .milliseconds(10)) }
        #expect(docker.lists == 2)

        d.set(false, forKey: RunnerWatchController.switchKey) // the switch turned off
        c.refresh()
        #expect(c.status == .off)
        #expect(sink.clears == 2)
    }

    @Test func theSwitchIsSearchableAndSitsOnTheWatchersPage() {
        for query in ["show my CI runners' jobs", "runner", "GitHub Actions", "Docker"] {
            #expect(SettingsSearch.hits(query).contains { $0.entry.page == .watchers && $0.entry.anchor == SettingsAnchor.ciRunners }, "\(query)")
        }
        #expect(SettingsNames.table.first { $0.name == SettingsNames.ciRunnersSwitch }?.page == .watchers)
        #expect(SettingsNames.anchors[SettingsNames.ciRunnersSwitch] == SettingsAnchor.ciRunners)
    }
}
