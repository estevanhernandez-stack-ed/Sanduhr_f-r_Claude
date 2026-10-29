import Foundation
import Testing
@testable import Sanduhr

/// Reset Sanduhr… stops what runs on its own and turns the writers off (review H1, H2). Every
/// folder here is a temp one; `ResetGate.$testOverride` holds only inside its scope, so the
/// tests beside these keep writing.
@Suite("Reset shutdown and gate")
struct ResetShutdownTests {
    private func scratch() -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-reset-gate-\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    private func exists(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: url.path) }

    // MARK: The gate

    @Test func gateIsOffOutsideItsScope() {
        #expect(!ResetGate.isOn)
        ResetGate.$testOverride.withValue(true) { #expect(ResetGate.isOn) }
        #expect(!ResetGate.isOn)
    }

    @Test func handoffWritesAreNoOpsWhileResetting() {
        let dir = scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("gone/desk-messages-state.json")
        ResetGate.$testOverride.withValue(true) { HandoffFiles.writeOwnerOnly(Data("{}".utf8), to: url) }
        #expect(!exists(url.deletingLastPathComponent()))
        HandoffFiles.writeOwnerOnly(Data("{}".utf8), to: url)
        #expect(exists(url))
    }

    @Test func historySnapshotSeatAndAccessWritesAreNoOpsWhileResetting() throws {
        let dir = scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = HistoryStore.Files(dir: dir)
        let snapshot = dir.appendingPathComponent("snapshot.json")
        let seats = dir.appendingPathComponent("seats.json")
        let access = dir.appendingPathComponent("mcp-access.json")
        ResetGate.$testOverride.withValue(true) {
            files.save(["five_hour": []], account: "Work")
            SnapshotWriter.writeError("network", accountRef: nil, to: snapshot)
            SeatSnapshots.write(Data("[]".utf8), to: seats)
            #expect(MCPAccess.write(Data("{}".utf8), to: access) == .unchanged)
        }
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path).isEmpty)
        // The same calls write once the gate is off: the test sees what it means to.
        files.save(["five_hour": []], account: "Work")
        SnapshotWriter.writeError("network", accountRef: nil, to: snapshot)
        #expect(exists(files.url(for: "Work")) && exists(snapshot))
    }

    @MainActor
    @Test func bandWriterStopsAndWritesNothingAfter() throws {
        let dir = scratch()
        let suite = "sanduhr.tests.reset-band.\(UUID().uuidString)"
        let d = try #require(UserDefaults(suiteName: suite))
        defer {
            try? FileManager.default.removeItem(at: dir)
            d.removePersistentDomain(forName: suite)
        }
        d.set(true, forKey: BandFile.watchersKey)
        let band = BandFileWriter(support: dir, defaults: d, reduceMotion: { false }, watchers: { [] })
        band.stop()
        #expect(band.isStopped)
        band.refresh()
        band.refresh(quitting: true)
        #expect(!exists(band.url))
        let live = BandFileWriter(support: dir, defaults: d, reduceMotion: { false }, watchers: { [] })
        live.refresh()
        #expect(exists(live.url))
    }

    @Test func themeWatcherStoppedDoesNotMakeTheFolderAgain() async throws {
        let root = scratch()
        defer { try? FileManager.default.removeItem(at: root) }
        let queue = DispatchQueue(label: "reset-watch-test")
        let stoppedDir = root.appendingPathComponent("themes", isDirectory: true)
        let liveDir = root.appendingPathComponent("live", isDirectory: true)
        let stopped = ThemeFolderWatcher(dir: stoppedDir, queue: queue) {}
        let live = ThemeFolderWatcher(dir: liveDir, queue: queue) {}
        queue.sync {}
        #expect(exists(stoppedDir) && exists(liveDir))
        queue.sync { stopped.stop() }
        #expect(queue.sync { stopped.isStopped })
        try FileManager.default.removeItem(at: stoppedDir)
        try FileManager.default.removeItem(at: liveDir)
        try await Task.sleep(nanoseconds: UInt64((ThemeFolderWatcher.settle + 0.5) * 1_000_000_000))
        queue.sync {}
        // A live watcher re-arms and makes its folder again; a stopped one doesn't.
        #expect(exists(liveDir))
        #expect(!exists(stoppedDir))
        withExtendedLifetime((stopped, live)) {}
    }

    @MainActor
    @Test func handoffWatchFollowerAndSeatsStop() throws {
        let dir = scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let watch = HandoffWatch(dir: dir)
        watch.add {}
        #expect(!watch.isStopped)
        watch.stop()
        #expect(watch.isStopped)
        watch.add {}
        #expect(watch.isStopped)

        let follower = AccountFollower()
        follower.apply(enabled: true)
        #expect(!follower.isStopped)
        follower.stop()
        #expect(follower.isStopped)
        follower.apply(enabled: true)
        #expect(follower.isStopped)

        let seats = SeatMeters(files: SeatSnapshots.Files(dir: dir))
        seats.stop()
        #expect(seats.isStopped)
        seats.sync()
        #expect(seats.startRound() == nil)
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path).isEmpty)
    }

    @Test func vaultStopsTriggering() {
        let dir = scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let vault = VaultService(vaultDir: dir.appendingPathComponent("vault").path, defaults: { MemoryDefaults() },
                                 writerVersion: "test", log: nil)
        #expect(!vault.isStopped)
        vault.stop()
        #expect(vault.isStopped)
        vault.trigger()
        #expect(!exists(dir.appendingPathComponent("vault")))
    }

    // MARK: The shutdown list

    @Test func shutdownStopsEveryServiceInOrderAndNamesWhatDidNot() {
        final class Fake { var stopped = false }
        var calls: [String] = []
        let a = Fake(), b = Fake(), stuck = Fake()
        let list = ResetShutdown(steps: [
            ResetShutdown.Step(name: "refresh", stop: { calls.append("refresh"); a.stopped = true }, isStopped: { a.stopped }),
            ResetShutdown.Step(name: "stuck", stop: { calls.append("stuck") }, isStopped: { stuck.stopped }),
            ResetShutdown.Step(name: "band", stop: { calls.append("band"); b.stopped = true }, isStopped: { b.stopped }),
        ])
        #expect(list.run() == ["stuck"])
        #expect(calls == ["refresh", "stuck", "band"])
        #expect(a.stopped && b.stopped)
    }
}
