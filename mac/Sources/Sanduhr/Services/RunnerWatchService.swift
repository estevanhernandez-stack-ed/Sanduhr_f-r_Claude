import Foundation

/// The two Docker calls CI watchers make (spec section 15). DockerSocket is the real one; tests
/// hand in a fake. Failures are DockerFailure.
protocol DockerAPI: Sendable {
    /// The list call's body, at most `DockerContainers.maxBytes`.
    func listContainers() async throws -> Data
    /// A runner's log, followed: the bytes of its stream, ending when Docker closes it or the
    /// consuming task is cancelled.
    func followLogs(id: String) async throws -> AsyncThrowingStream<Data, Error>
}

/// Where runner lines go: the watcher store, which applies them to the board.
@MainActor
protocol RunnerSink: AnyObject {
    func apply(key: String, runner: String, line: RunnerLine)
    func gone(key: String)
    func clear()
}

/// CI watchers (spec section 15; Windows' RunnerWatchService): while it is on, lists Docker's
/// containers every `listEvery`, follows each running runner's log on its own task, and hands each
/// line the replay gate lets through to the sink. Docker down or refusing is a status line,
/// retried every `retryEvery`, never an error. Logs nothing: no job, runner or container name.
///
/// Everything runs on the main actor, so a delivery and a cancel never interleave: a follow that
/// was cancelled delivers nothing after that, and the sink hears `gone` or `clear` only after the
/// cancel. A line therefore never reaches the sink after the end that retired its follow (a card
/// could otherwise stay running for good).
@MainActor
final class RunnerWatchService {
    @MainActor
    private final class Follow {
        let id: String
        let key: String
        var cancelled = false
        var finished = false
        var task: Task<Void, Never>?
        var gate: RunnerFollow?
        var frames = DockerLogFrames()

        init(id: String, key: String) { self.id = id; self.key = key }

        func cancel() {
            cancelled = true
            task?.cancel()
        }
    }

    private let docker: DockerAPI
    private let enabled: () -> Bool
    private let sink: RunnerSink
    private let now: () -> Date
    private let replayWait: Duration
    private let listEvery: Duration
    private let retryEvery: Duration

    private var follows: [String: Follow] = [:]
    private var loop: Task<Void, Never>?
    private var disposed = false
    private(set) var status = RunnerStatus.off

    /// The status line changed.
    var onStatusChange: (() -> Void)?

    init(docker: DockerAPI, enabled: @escaping () -> Bool, sink: RunnerSink,
         now: @escaping () -> Date = Date.init, replayWait: Duration = .seconds(2),
         listEvery: Duration = .seconds(30), retryEvery: Duration = .seconds(60)) {
        self.docker = docker
        self.enabled = enabled
        self.sink = sink
        self.now = now
        self.replayWait = replayWait
        self.listEvery = listEvery
        self.retryEvery = retryEvery
    }

    /// The switch changed, the Desk started or stopped, or the app started: start or end the loop.
    /// Idempotent: already running with the switch on does nothing.
    func refresh() {
        guard !disposed else { return }
        if enabled() {
            guard loop == nil else { return }
            loop = Task { await self.run() }
            return
        }
        stopLoop()
        stopAllFollows(announce: false)
        sink.clear()
        setStatus(.off)
    }

    func dispose() {
        disposed = true
        stopLoop()
        stopAllFollows(announce: false)
    }

    private func stopLoop() {
        loop?.cancel()
        loop = nil
    }

    private func run() async {
        while !Task.isCancelled {
            await pollOnce()
            let failing = [.dockerDown, .dockerDenied, .dockerRefused].contains(status.kind)
            do { try await Task.sleep(for: failing ? retryEvery : listEvery) } catch { return }
        }
    }

    /// One list: the status, a follow for each new runner, an end for each runner that left.
    func pollOnce() async {
        let runners: [DockerContainer]
        do {
            runners = DockerContainers.runners(DockerContainers.parse(try await docker.listContainers()))
        } catch {
            guard !Task.isCancelled, !disposed, !(error is CancellationError) else { return }
            stopAllFollows(announce: true)
            let kind: RunnerStatusKind = switch error as? DockerFailure {
            case .denied: .dockerDenied
            case .refused: .dockerRefused
            default: .dockerDown
            }
            setStatus(RunnerStatus(kind: kind))
            return
        }
        // Rechecked after the await: switching off and disposing cancel first.
        guard !Task.isCancelled, !disposed else { return }

        // The follow table is keyed by the container's own name, never the shared service label.
        let present = Set(runners.map(\.key))
        var left: [Follow] = []
        for (key, f) in follows where !present.contains(key) {
            follows[key] = nil
            left.append(f)
        }
        for r in runners {
            if let existing = follows[r.key] {
                if existing.id == r.id && !existing.finished { continue }
                if existing.id != r.id {
                    // Recreated under the same name: follow the new one. The old card ends here,
                    // before the new follow exists, so it ends even if the new log can't be read.
                    existing.cancel()
                    sink.gone(key: existing.key)
                }
            }
            let f = Follow(id: r.id, key: r.key)
            follows[r.key] = f
            let runner = r.name
            f.task = Task { await self.runFollow(f, runner: runner) }
        }
        retire(left, announce: true)
        setStatus(runners.isEmpty
            ? RunnerStatus(kind: .noRunners)
            : RunnerStatus(kind: .watching, runners: runners.map(\.name)))
    }

    /// The tests' way to wait for the follows the last poll started (each fake stream ends).
    func whenFollowsSettle() async {
        for f in Array(follows.values) { await f.task?.value }
    }

    private func runFollow(_ f: Follow, runner: String) async {
        defer { f.finished = true }
        let stream: AsyncThrowingStream<Data, Error>
        do { stream = try await docker.followLogs(id: f.id) } catch { return }
        f.gate = RunnerFollow(opened: now())
        // Docker replays the tail; a runner that prints nothing new would never end the replay.
        let wait = replayWait
        let timer = Task { [weak self] in
            do { try await Task.sleep(for: wait) } catch { return }
            self?.deliver(f, runner: runner) { $0.endReplay() }
        }
        defer { timer.cancel() }
        do {
            for try await data in stream {
                for text in f.frames.push(data) {
                    guard let line = RunnerLog.parse(text) else { continue }
                    deliver(f, runner: runner) { $0.feed(line) }
                }
            }
        } catch {
            // The next poll follows again if the runner is still there.
            return
        }
        deliver(f, runner: runner) { $0.endReplay() } // a stream that ended before the wait ran out
    }

    /// Runs one step of the runner's replay gate and applies what it lets through. A follow that
    /// was cancelled delivers nothing.
    private func deliver(_ f: Follow, runner: String, _ step: (inout RunnerFollow) -> [RunnerLine]) {
        guard !f.cancelled, !disposed, var gate = f.gate else { return }
        let lines = step(&gate)
        f.gate = gate
        for line in lines { sink.apply(key: f.key, runner: runner, line: line) }
    }

    /// Stops every follow. `announce`: the sink hears `gone` for each, so an open card ends as
    /// finished (Docker went away); a switch-off clears the sink instead.
    private func stopAllFollows(announce: Bool) {
        let all = Array(follows.values)
        follows.removeAll()
        retire(all, announce: announce)
    }

    private func retire(_ list: [Follow], announce: Bool) {
        for f in list {
            f.cancel()
            if announce { sink.gone(key: f.key) }
        }
    }

    private func setStatus(_ new: RunnerStatus) {
        guard !disposed, !Task.isCancelled, new != status else { return }
        status = new
        onStatusChange?()
    }
}
