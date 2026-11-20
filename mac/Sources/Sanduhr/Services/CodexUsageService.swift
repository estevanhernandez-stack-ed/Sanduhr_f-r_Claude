import Foundation
import Observation
import os

private let codexLog = Logger(subsystem: "com.626labs.sanduhr", category: "codex")

/// Where a read runs and where its answer lands. The app reads off the main thread and publishes
/// on it; tests run both inline, or hold the work to run it later, so nothing races a timer.
protocol CodexReadRunner {
    func run(_ work: @escaping () -> CodexPollOutput, then done: @escaping (CodexPollOutput) -> Void)
}

/// One poll's answer, with the cache the read left behind.
struct CodexPollOutput {
    var result: CodexReadResult?
    var cache: CodexReadCache
}

struct BackgroundCodexRunner: CodexReadRunner {
    func run(_ work: @escaping () -> CodexPollOutput, then done: @escaping (CodexPollOutput) -> Void) {
        DispatchQueue.global(qos: .utility).async {
            let out = work()
            DispatchQueue.main.async { done(out) }
        }
    }
}

/// The run a read was issued in. Stop bumps it, and a read queued or in flight from an earlier run
/// sees the change and does no IO (the work checks it before it reads; the answer is dropped).
final class CodexGeneration: @unchecked Sendable {
    private let lock = NSLock()
    private var value = 0
    var current: Int { lock.withLock { value } }
    func bump() { lock.withLock { value += 1 } }
}

/// Codex limits, kept current (agent layer spec, sections 1 and 4; agent sources design, section 1):
/// reads the newest rollout event every 30 seconds while Codex is on and publishes the limits in
/// Claude's tier shape, their `as_of`, freshness and the empty states. No file watch, no network,
/// nothing under demo mode. History, snapshot and alerts are not here (item 4). Main thread only.
@Observable
@MainActor
final class CodexUsageService {
    static let pollEvery: TimeInterval = 30

    enum Freshness: Equatable { case fresh, stale }

    /// What the Codex card says when there are no limits to draw (spec, section 4).
    enum EmptyState: Equatable {
        /// No Codex home: "Codex isn't installed on this Mac."
        case notInstalled
        /// Installed, nothing in the logs yet: "Run Codex once to see your limits."
        case noLimitsYet
        /// Every rollout tried failed: "Couldn't read Codex's logs."
        case couldNotRead
    }

    /// Codex's limits as tiers (`.fiveHour`, `.sevenDay`, utilization 0 to 100); nil with no reading.
    private(set) var codexUsage: UsageResponse?
    /// When Codex wrote the event the limits came from.
    private(set) var asOf: Date?
    /// Fresh when any tier is (see `freshTiers`); nil with no reading.
    private(set) var freshness: Freshness?
    /// The tiers whose number is current: the event is under 30 minutes old, or that tier's reset passed.
    private(set) var freshTiers: Set<Tier> = []
    private(set) var planType: String?
    /// Set when the service has read and found nothing to draw; nil while off or with a reading.
    private(set) var emptyState: EmptyState?
    /// Polling (Codex on, not demo, not reset).
    private(set) var running = false

    @ObservationIgnored private let homes: () -> [URL]
    @ObservationIgnored private let fileSystem: CodexFileSystem
    @ObservationIgnored private let ticker: AudioBarsTicker
    @ObservationIgnored private let clock: () -> Date
    @ObservationIgnored private let runner: CodexReadRunner
    @ObservationIgnored private let resetDone: () -> Bool
    @ObservationIgnored private let log: (String) -> Void
    @ObservationIgnored private let generation = CodexGeneration()
    @ObservationIgnored private var cache = CodexReadCache()
    @ObservationIgnored private var inFlight = false
    @ObservationIgnored private var loggedWindows = Set<Int>()

    init(homes: @escaping () -> [URL] = CodexHomes.live,
         fileSystem: CodexFileSystem = CodexDisk(),
         ticker: AudioBarsTicker = TimerTicker(),
         clock: @escaping () -> Date = Date.init,
         runner: CodexReadRunner = BackgroundCodexRunner(),
         resetDone: @escaping () -> Bool = { ResetGate.isOn },
         log: @escaping (String) -> Void = { codexLog.notice("\($0, privacy: .public)") }) {
        self.homes = homes
        self.fileSystem = fileSystem
        self.ticker = ticker
        self.clock = clock
        self.runner = runner
        self.resetDone = resetDone
        self.log = log
    }

    /// Whether the service should poll: Codex ticked in the setup and not demo mode (demo never
    /// reads `~/.codex`). A Mac with only Claude on never polls.
    static func wants(setup: SetupSettings?, sources: AgentSourceSettings, demo: Bool) -> Bool {
        !demo && sources.isOn(.codex, setup: setup)
    }

    /// Starts or stops polling. Idempotent: an unchanged answer neither restarts nor reads.
    func update(on: Bool) {
        if on && !running { start() } else if !on && running { stop() }
    }

    private func start() {
        guard !resetDone() else { return }
        running = true
        ticker.start(interval: Self.pollEvery) { [weak self] in
            MainActor.assumeIsolated { self?.poll() }
        }
        poll()
    }

    /// Stops polling and clears what it published. A tick or read from before returns without IO.
    func stop() {
        generation.bump()
        ticker.stop()
        running = false
        inFlight = false
        cache = CodexReadCache()
        set(usage: nil, asOf: nil, freshness: nil, freshTiers: [], plan: nil, empty: nil)
    }

    /// One read now (the timer's tick). Skipped while off, after Reset, or with a read still in flight.
    func poll() {
        guard running, !inFlight, !resetDone() else { return }
        inFlight = true
        let issued = generation.current
        let generation = self.generation
        let fileSystem = self.fileSystem
        let homes = self.homes
        let cache = self.cache
        let log = self.log
        runner.run({
            guard generation.current == issued else { return CodexPollOutput(result: nil, cache: cache) }
            var c = cache
            let reader = CodexLimitsReader(homes: homes(), fileSystem: fileSystem, log: log)
            let result = reader.read(cache: &c)
            return CodexPollOutput(result: result, cache: c)
        }, then: { [weak self] out in
            MainActor.assumeIsolated {
                guard let self, generation.current == issued else { return }
                self.inFlight = false
                guard let result = out.result else { return }
                self.cache = out.cache
                self.publish(result)
            }
        })
    }

    private func publish(_ result: CodexReadResult) {
        guard result.status == .ok, let reading = result.reading else {
            let empty: EmptyState = switch result.status {
            case .notInstalled: .notInstalled
            case .unreadable: .couldNotRead
            case .noLimits, .ok: .noLimitsYet
            }
            set(usage: nil, asOf: nil, freshness: nil, freshTiers: [], plan: nil, empty: empty)
            return
        }
        let now = clock()
        let usage = CodexLimits.toUsage(reading, now: now) { [self] minutes in
            if loggedWindows.insert(minutes).inserted {
                log("codex: unknown window of \(minutes) minutes ignored")
            }
        }
        let fresh = CodexLimits.freshTiers(reading, now: now)
        set(usage: usage, asOf: reading.asOf, freshness: fresh.isEmpty ? .stale : .fresh,
            freshTiers: fresh, plan: reading.planType, empty: nil)
    }

    /// Assigns only what changed, so observers redraw on a real change only.
    private func set(usage: UsageResponse?, asOf: Date?, freshness: Freshness?, freshTiers: Set<Tier>,
                     plan: String?, empty: EmptyState?) {
        if codexUsage != usage { codexUsage = usage }
        if self.asOf != asOf { self.asOf = asOf }
        if self.freshness != freshness { self.freshness = freshness }
        if self.freshTiers != freshTiers { self.freshTiers = freshTiers }
        if planType != plan { planType = plan }
        if emptyState != empty { emptyState = empty }
    }
}
