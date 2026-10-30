import Foundation
import Testing
@testable import Sanduhr

/// The runner watch loop against a fake Docker (spec section 15; Windows' RunnerWatchServiceTests).
/// No socket: the transport is a fake that serves lists and log streams.
@MainActor
@Suite("Runner watch service")
struct RunnerWatchServiceTests {
    nonisolated static let id1 = String(repeating: "1", count: 64)
    nonisolated static let id2 = String(repeating: "2", count: 64)
    nonisolated static let now = Date(timeIntervalSince1970: 1_792_000_000)

    // MARK: Fixtures

    nonisolated private static func list(_ cs: [(id: String, service: String, image: String)]) -> Data {
        let items = cs.map {
            #"{"Id":"\#($0.id)","Names":["/\#($0.service)"],"Image":"\#($0.image)","State":"running","Labels":{"com.docker.compose.service":"\#($0.service)"}}"#
        }
        return Data(("[" + items.joined(separator: ",") + "]").utf8)
    }

    /// Scaled replicas: the same service label, each its own container name.
    nonisolated private static func replicas(_ cs: [(id: String, name: String)]) -> Data {
        let items = cs.map {
            #"{"Id":"\#($0.id)","Names":["/\#($0.name)"],"Image":"acme/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"runner"}}"#
        }
        return Data(("[" + items.joined(separator: ",") + "]").utf8)
    }

    nonisolated private static func one() -> Data { list([(id1, "echo-1", "acme-gh-runner:2.337.0")]) }

    private static func frames(_ lines: [String]) -> Data {
        var out = Data()
        for l in lines {
            let payload = Data((l + "\n").utf8)
            var header: [UInt8] = [1, 0, 0, 0]
            withUnsafeBytes(of: UInt32(payload.count).bigEndian) { header.append(contentsOf: $0) }
            out.append(contentsOf: header)
            out.append(payload)
        }
        return out
    }

    private static let utc: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        return f
    }()

    private static func line(_ at: Date, _ text: String) -> String {
        utc.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSSSSSS'Z'"
        let docker = utc.string(from: at)
        utc.dateFormat = "yyyy-MM-dd HH:mm:ss'Z'"
        return "\(docker) \(utc.string(from: at)): \(text)"
    }

    nonisolated private static func finite(_ data: Data) -> AsyncThrowingStream<Data, Error> {
        AsyncThrowingStream { c in
            if !data.isEmpty { c.yield(data) }
            c.finish()
        }
    }

    // MARK: Fakes

    final class FakeDocker: DockerAPI, @unchecked Sendable {
        private let lock = NSLock()
        var list: @Sendable () throws -> Data = { Data("[]".utf8) }
        var listAsync: (@Sendable () async throws -> Data)?
        var logs: [String: [Data]] = [:]
        var open: (@Sendable (String) throws -> AsyncThrowingStream<Data, Error>)?
        private var followCount = 0
        var follows: Int { lock.withLock { followCount } }

        func listContainers() async throws -> Data {
            if let listAsync { return try await listAsync() }
            return try list()
        }

        func followLogs(id: String) async throws -> AsyncThrowingStream<Data, Error> {
            lock.withLock { followCount += 1 }
            if let open { return try open(id) }
            let bytes: Data = lock.withLock {
                guard var q = logs[id], !q.isEmpty else { return Data() }
                let first = q.removeFirst()
                logs[id] = q
                return first
            }
            return RunnerWatchServiceTests.finite(bytes)
        }
    }

    /// A log stream that stays open until finished: what it is given, then whatever is pushed.
    final class Held: @unchecked Sendable {
        let stream: AsyncThrowingStream<Data, Error>
        private let continuation: AsyncThrowingStream<Data, Error>.Continuation
        private let lock = NSLock()
        private var done = false
        var terminated: Bool { lock.withLock { done } }

        init(_ initial: Data = Data()) {
            let (s, c) = AsyncThrowingStream<Data, Error>.makeStream()
            stream = s
            continuation = c
            if !initial.isEmpty { c.yield(initial) }
            c.onTermination = { [lock] _ in lock.withLock { self.done = true } }
        }

        func push(_ d: Data) { continuation.yield(d) }
        func finish() { continuation.finish() }
    }

    final class Sink: RunnerSink {
        var board = WatcherBoard()
        var glows = 0, clears = 0, applies = 0, onlines = 0

        func apply(key: String, runner: String, line: RunnerLine) {
            applies += 1
            if case .online = line { onlines += 1 }
            if board.applyRunner(key: key, runner: runner, line: line, now: RunnerWatchServiceTests.now).failed { glows += 1 }
        }
        func gone(key: String) { board.runnerGone(key: key, now: RunnerWatchServiceTests.now) }
        func clear() { board.clear(.runner); clears += 1 }

        func card(_ id: String) -> Watcher? { board.watcher(id) }
        var count: Int { board.watchers.count }
    }

    final class Flag { var on: Bool; init(_ on: Bool) { self.on = on } }

    private func service(_ d: FakeDocker, _ s: Sink, on: Flag = Flag(true), replayWait: Duration = .seconds(2)) -> RunnerWatchService {
        RunnerWatchService(docker: d, enabled: { on.on }, sink: s, now: { Self.now }, replayWait: replayWait)
    }

    /// Waits for `done` with a deadline, never an unbounded sleep. The deadline catches a hang; it is
    /// generous because a busy CI runner has held the main actor for more than 10 s.
    private func until(_ what: String = "condition", _ done: () -> Bool) async throws {
        let deadline = ContinuousClock.now + .seconds(60)
        while !done() {
            #expect(ContinuousClock.now < deadline, "timed out waiting for \(what)")
            if ContinuousClock.now >= deadline { return }
            try await Task.sleep(for: .milliseconds(5))
        }
    }

    // MARK: Status

    @Test func listsRunnersOnlyAndSaysSo() async {
        let d = FakeDocker()
        d.list = { Self.list([(Self.id1, "alpha-1", "acme-gh-runner:2.337.0"), (Self.id2, "egress-proxy", "acme-egress-proxy:1")]) }
        let svc = service(d, Sink())
        await svc.pollOnce()
        #expect(svc.status == RunnerStatus(kind: .watching, runners: ["alpha-1"]))
        await svc.whenFollowsSettle()
        #expect(d.follows == 1)
    }

    @Test func noRunners() async {
        let svc = service(FakeDocker(), Sink())
        await svc.pollOnce()
        #expect(svc.status.kind == .noRunners)
    }

    @Test func dockerDown() async {
        let d = FakeDocker()
        d.list = { throw DockerFailure.down }
        let svc = service(d, Sink())
        await svc.pollOnce()
        #expect(svc.status.kind == .dockerDown)
    }

    @Test func anErrorStatusIsNotDockerBeingDown() async {
        let d = FakeDocker()
        d.list = { throw DockerFailure.refused }
        let svc = service(d, Sink())
        await svc.pollOnce()
        #expect(svc.status.kind == .dockerRefused)
        #expect(svc.status.text == "Docker answered but refused the request.")
    }

    @Test func aMissingErrorTypeIsDockerDown() async {
        let d = FakeDocker()
        d.list = { throw URLError(.cannotConnectToHost) }
        let svc = service(d, Sink())
        await svc.pollOnce()
        #expect(svc.status.kind == .dockerDown)
    }

    @Test func accessDeniedShowsTheAccountLine() async {
        let d = FakeDocker()
        d.list = { throw DockerFailure.denied }
        let svc = service(d, Sink())
        var raised = 0
        svc.onStatusChange = { raised += 1 }
        await svc.pollOnce()
        await svc.pollOnce()
        #expect(svc.status.text == "Docker refused the connection. Check that your account can use Docker's socket.")
        #expect(raised == 1) // once per change, not per retry
    }

    // MARK: Cards

    @Test func aLiveJobBecomesACardAndAFailureGlows() async {
        let d = FakeDocker()
        d.list = { Self.one() }
        d.logs[Self.id1] = [Self.frames([Self.line(Self.now, "Running job: build"),
                                         Self.line(Self.now.addingTimeInterval(5), "Job build completed with result: Failed")])]
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(sink.card("r:echo-1:build")?.state == .failed)
        #expect(sink.glows == 1)
    }

    @Test func replayedFinishedJobsPutUpNothing() async {
        let d = FakeDocker()
        d.list = { Self.one() }
        let hour = Self.now.addingTimeInterval(-3600)
        d.logs[Self.id1] = [Self.frames([Self.line(hour, "Running job: build"),
                                         Self.line(hour.addingTimeInterval(60), "Job build completed with result: Succeeded")])]
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(sink.board.watchers.isEmpty)
    }

    @Test func streamEndRefollowsWithoutDuplicates() async {
        let d = FakeDocker()
        d.list = { Self.one() }
        let open = Self.frames([Self.line(Self.now.addingTimeInterval(-300), "Running job: build")])
        d.logs[Self.id1] = [open, open] // the stream ends, the same tail comes back
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(sink.count == 1)
        #expect(sink.card("r:echo-1:build")?.state == .running)
        #expect(d.follows == 2)
    }

    @Test func aRunnerThatLeavesEndsItsCard() async {
        let d = FakeDocker()
        let listed = Flag(true)
        d.list = { listed.on ? Self.one() : Data("[]".utf8) }
        d.logs[Self.id1] = [Self.frames([Self.line(Self.now, "Running job: build")])]
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        listed.on = false
        await svc.pollOnce()
        #expect(sink.card("r:echo-1:build")?.state == .finished)
    }

    @Test func replicasThatShareALabelDoNotEndEachOthersCards() async {
        let d = FakeDocker()
        d.list = { Self.replicas([(Self.id1, "ci-runner-1"), (Self.id2, "ci-runner-2")]) }
        d.logs[Self.id1] = [Self.frames([Self.line(Self.now, "Running job: build")])]
        d.logs[Self.id2] = [Self.frames([Self.line(Self.now, "Running job: test"),
                                         Self.line(Self.now.addingTimeInterval(5), "Job test completed with result: Failed")])]
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(svc.status.text == "Watching 2 runners: ci-runner-1, ci-runner-2")
        #expect(sink.card("r:ci-runner-1:build")?.state == .running) // the sibling's start and end left it alone
        #expect(sink.card("r:ci-runner-2:test")?.state == .failed)
        #expect(sink.glows == 1)
    }

    @Test func aSiblingComingOnlineLeavesTheBusyOnesCardRunning() async throws {
        let d = FakeDocker()
        let both = Flag(false)
        d.list = { both.on ? Self.replicas([(Self.id1, "ci-runner-1"), (Self.id2, "ci-runner-2")]) : Self.replicas([(Self.id1, "ci-runner-1")]) }
        let busy = Held(Self.frames([Self.line(Self.now, "Running job: build")])) // stays open: a job in progress
        d.open = { id in id == Self.id1 ? busy.stream : Self.finite(Data()) } // the sibling is idle: its follow opens with online
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        try await until("the first card") { sink.card("r:ci-runner-1:build") != nil }
        both.on = true
        await svc.pollOnce()
        try await until("two onlines") { sink.onlines == 2 } // one for the first runner's live start, one for the idle sibling
        #expect(d.follows == 2)
        #expect(sink.card("r:ci-runner-1:build")?.state == .running)
        busy.finish()
    }

    @Test func aSiblingThatLeavesEndsOnlyItsOwnCard() async throws {
        let d = FakeDocker()
        let both = Flag(true)
        d.list = { both.on ? Self.replicas([(Self.id1, "ci-runner-1"), (Self.id2, "ci-runner-2")]) : Self.replicas([(Self.id1, "ci-runner-1")]) }
        let s1 = Held(Self.frames([Self.line(Self.now, "Running job: build")]))
        let s2 = Held(Self.frames([Self.line(Self.now, "Running job: test")]))
        d.open = { id in id == Self.id1 ? s1.stream : s2.stream }
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        try await until("two cards") { sink.count == 2 }
        both.on = false
        await svc.pollOnce()
        #expect(sink.card("r:ci-runner-1:build")?.state == .running)
        #expect(sink.card("r:ci-runner-2:test")?.state == .finished)
        s1.finish()
    }

    @Test func aRecreatedContainerKeepsItsNameAndIsFollowedAgain() async throws {
        let d = FakeDocker()
        let current = Box(Self.id1)
        d.list = { Self.replicas([(current.value, "ci-runner-1")]) }
        let old = Held(Self.frames([Self.line(Self.now, "Running job: build")]))
        let fresh = Held()
        d.open = { id in id == Self.id1 ? old.stream : fresh.stream }
        let sink = Sink()
        let svc = service(d, sink, replayWait: .milliseconds(50))
        await svc.pollOnce()
        try await until("the card") { sink.card("r:ci-runner-1:build") != nil }
        current.value = Self.id2 // Compose recreated it: same name, new id
        await svc.pollOnce()
        try await until("the old stream to be released") { old.terminated }
        try await until("the new follow") { d.follows == 2 }
        #expect(sink.card("r:ci-runner-1:build")?.state == .finished) // the old follow's end closes the old card
        fresh.finish()
    }

    final class Box { var value: String; init(_ v: String) { value = v } }

    @Test func aRecreatedContainerWhoseLogCannotBeReadStillEndsTheOldCard() async throws {
        let d = FakeDocker()
        let current = Box(Self.id1)
        d.list = { Self.replicas([(current.value, "ci-runner-1")]) }
        let old = Held(Self.frames([Self.line(Self.now, "Running job: build")]))
        d.open = { id in
            if id == Self.id1 { return old.stream }
            throw DockerFailure.refused // the new container's logs call fails
        }
        let sink = Sink()
        let svc = service(d, sink, replayWait: .milliseconds(50))
        await svc.pollOnce()
        try await until("the card") { sink.card("r:ci-runner-1:build") != nil }
        current.value = Self.id2
        await svc.pollOnce()
        await svc.whenFollowsSettle() // the new follow failed without delivering a line
        #expect(sink.card("r:ci-runner-1:build")?.state == .finished) // the replaced follow's end closed it
        old.finish()
    }

    @Test func dockerGoingDownEndsTheOpenCardsAndTheyStayEnded() async {
        let d = FakeDocker()
        let up = Flag(true)
        d.list = {
            if up.on { return Self.one() }
            throw DockerFailure.down
        }
        d.logs[Self.id1] = [Self.frames([Self.line(Self.now, "Running job: build")])]
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(sink.card("r:echo-1:build")?.state == .running)
        up.on = false
        await svc.pollOnce()
        #expect(svc.status.kind == .dockerDown)
        #expect(sink.card("r:echo-1:build")?.state == .finished)
        up.on = true
        d.list = { Data("[]".utf8) }
        await svc.pollOnce()
        #expect(!sink.board.watchers.contains { !$0.state.isEnded })
    }

    @Test func aRefollowThatReplaysTheJobAsEndedEndsTheCard() async {
        let d = FakeDocker()
        d.list = { Self.one() }
        d.logs[Self.id1] = [
            Self.frames([Self.line(Self.now, "Running job: build")]),
            Self.frames([Self.line(Self.now.addingTimeInterval(-120), "Running job: build"),
                         Self.line(Self.now.addingTimeInterval(-60), "Job build completed with result: Succeeded")]), // ended 60 s ago
        ]
        let sink = Sink()
        let svc = service(d, sink)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(sink.card("r:echo-1:build")?.state == .running)
        await svc.pollOnce()
        await svc.whenFollowsSettle()
        #expect(sink.card("r:echo-1:build")?.state == .finished)
    }

    @Test func theReplayTimerPutsUpAnOpenJobWithoutMoreBytes() async throws {
        let held = Held(Self.frames([Self.line(Self.now.addingTimeInterval(-300), "Running job: build")]))
        let d = FakeDocker()
        d.list = { Self.one() }
        d.open = { _ in held.stream }
        let sink = Sink()
        let svc = service(d, sink, replayWait: .milliseconds(50))
        await svc.pollOnce()
        try await until("the card") { sink.card("r:echo-1:build") != nil }
        #expect(sink.card("r:echo-1:build")?.state == .running)
        held.finish()
        await svc.whenFollowsSettle()
        #expect(sink.count == 1)
    }

    // MARK: Switching and cancelling

    @Test func switchingOffClearsAndSaysNothing() async {
        let on = Flag(true)
        let sink = Sink()
        let svc = service(FakeDocker(), sink, on: on)
        await svc.pollOnce()
        #expect(svc.status.kind == .noRunners)
        on.on = false
        svc.refresh()
        #expect(svc.status == .off)
        #expect(sink.clears == 1)
    }

    @Test func nothingLandsAfterSwitchingOff() async throws {
        let held = Held()
        let d = FakeDocker()
        d.list = { Self.one() }
        d.open = { _ in held.stream }
        let on = Flag(true)
        let sink = Sink()
        let svc = service(d, sink, on: on)
        await svc.pollOnce()
        on.on = false
        svc.refresh()
        #expect(sink.clears == 1)
        held.push(Self.frames([Self.line(Self.now, "Running job: build")])) // the reader hears of it after the clear
        held.finish()
        try await until("the stream to be released") { held.terminated }
        try await Task.sleep(for: .milliseconds(30))
        #expect(sink.applies == 0)
        #expect(sink.count == 0)
    }

    @Test func switchingOnOffOnRunsOneLoopAndOneCard() async throws {
        let d = FakeDocker()
        d.list = { Self.one() }
        d.logs[Self.id1] = [Self.frames([Self.line(Self.now, "Running job: build")]),
                            Self.frames([Self.line(Self.now, "Running job: build")])]
        let on = Flag(true)
        let sink = Sink()
        let svc = service(d, sink, on: on)
        svc.refresh()
        svc.refresh() // already on: no second loop
        try await until("the card") { sink.card("r:echo-1:build") != nil }
        #expect(d.follows == 1)
        on.on = false
        svc.refresh()
        #expect(svc.status == .off)
        #expect(sink.count == 0)
        on.on = true
        svc.refresh()
        try await until("the card again") { sink.card("r:echo-1:build") != nil }
        #expect(d.follows == 2)
        #expect(sink.count == 1)
        #expect(sink.card("r:echo-1:build")?.state == .running)
        svc.dispose()
    }

    @Test func aListThatReturnsAfterTheLoopWasCancelledSaysNothing() async {
        let (entered, signal) = AsyncStream<Void>.makeStream()
        let (release, open) = AsyncStream<Void>.makeStream()
        let d = FakeDocker()
        d.listAsync = {
            signal.yield()
            for await _ in release { break }
            return Self.one()
        }
        let svc = service(d, Sink())
        let poll = Task { await svc.pollOnce() }
        for await _ in entered { break }
        poll.cancel()
        open.yield()
        await poll.value
        #expect(svc.status == .off)
        #expect(d.follows == 0)
    }

    @Test func aListThatReturnsAfterDisposeSaysNothing() async {
        let (entered, signal) = AsyncStream<Void>.makeStream()
        let (release, open) = AsyncStream<Void>.makeStream()
        let d = FakeDocker()
        d.listAsync = {
            signal.yield()
            for await _ in release { break }
            return Self.one()
        }
        let svc = service(d, Sink())
        let poll = Task { await svc.pollOnce() }
        for await _ in entered { break }
        svc.dispose()
        open.yield()
        await poll.value
        #expect(svc.status == .off)
        #expect(d.follows == 0)
    }
}
