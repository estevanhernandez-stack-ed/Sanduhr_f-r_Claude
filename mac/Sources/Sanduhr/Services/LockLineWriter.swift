import AppKit
import Foundation

/// Keeps `~/Library/Application Support/Sanduhr/lock-line.txt` (#137) holding the composed lock
/// screen line, or nothing. The root helper (LockLineDaemon) puts it on the lock screen
/// (docs/lock-screen-line-spec.md); this keeps the text ready, and rewrites the file only when
/// the text changed.
final class LockLineWriter {
    static let fileName = "lock-line.txt"
    /// Usage older than this reads as unavailable on the lock screen.
    static let staleAfter: TimeInterval = 20 * 60
    static let refreshInterval: TimeInterval = 5 * 60

    static var url: URL {
        SnapshotWriter.url.deletingLastPathComponent().appendingPathComponent(fileName)
    }

    // MARK: Inputs (pure)

    /// The status a fetch leaves when all is well; anything else (an error, signed out, no tiers)
    /// reads as unavailable. A fetch in flight does not.
    static func isNormal(_ status: UsageViewModel.StatusMessage) -> Bool {
        switch status {
        case .idle, .refreshing: true
        default: false
        }
    }

    /// The shown usage is the highest-utilization tier (ties go to the earlier tier in
    /// `Tier.allCases`, the session first) with that tier's own reset time.
    static func inputs(usage: UsageResponse?, lastUpdated: Date?, statusNormal: Bool, now: Date,
                       prefix: String, show: LockLine.Slots, waitingCount: Int) -> LockLine.Inputs {
        var best: (percent: Double, resetsAt: String?)?
        for tier in Tier.allCases {
            guard let t = usage?.tiers[tier], let u = t.utilization else { continue }
            if best == nil || u > best!.percent { best = (u, t.resetsAt) }
        }
        let stale: Bool
        if let lastUpdated { stale = now.timeIntervalSince(lastUpdated) > staleAfter } else { stale = true }
        return LockLine.Inputs(prefix: prefix,
                               usagePercent: best.map { Int($0.percent) },
                               resetsAt: best?.resetsAt.flatMap(ISO8601.parse),
                               usageStale: stale || !statusNormal,
                               waitingCount: waitingCount,
                               show: show)
    }

    // MARK: Plan

    enum Plan: Equatable { case compose, clear, nothing }

    /// On composes, off clears (once), and demo mode never writes: its numbers are samples.
    static func plan(on: Bool, demo: Bool) -> Plan {
        if demo { return .nothing }
        return on ? .compose : .clear
    }

    // MARK: Writing

    private let writeFile: (String, URL) throws -> Void
    private let readFile: (URL) -> String?
    private let target: URL
    private var lastWritten: String?

    init(target: URL = LockLineWriter.url,
         writeFile: @escaping (String, URL) throws -> Void = LockLineWriter.atomicWrite,
         readFile: @escaping (URL) -> String? = { try? String(contentsOf: $0, encoding: .utf8) }) {
        self.target = target
        self.writeFile = writeFile
        self.readFile = readFile
    }

    /// Writes `line` when it differs from what the file holds. After a reset, nothing comes back.
    func write(_ line: String) {
        guard !ResetGate.isOn else { return }
        if lastWritten == nil { lastWritten = readFile(target) ?? "" }
        guard line != lastWritten else { return }
        do {
            try writeFile(line, target)
            lastWritten = line
        } catch {
            NSLog("Sanduhr lock line write failed (\(type(of: error)))")
        }
    }

    /// The switch is off: an empty file, once (nothing at all when it was never written).
    func clear() { write("") }

    static func atomicWrite(_ text: String, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url, options: .atomic)
    }
}

/// Settings keys for the Lock Screen page (the Desk defaults suite).
enum LockLineKeys {
    static let on = "lockLine"
    static let prefix = "lockLinePrefix"
    static let usage = "lockLineUsage"
    static let reset = "lockLineReset"
    static let waiting = "lockLineWaiting"

    static func slots(in d: UserDefaults = .desk) -> LockLine.Slots {
        LockLine.Slots(usage: d.object(forKey: usage) as? Bool ?? true,
                       reset: d.object(forKey: reset) as? Bool ?? true,
                       waiting: d.object(forKey: waiting) as? Bool ?? true)
    }
}

/// Recomposes the line every five minutes while the session is active and on the way out of it
/// (session resign, screens asleep), so it is fresh as the screen locks. No timer work while the
/// session is away.
@MainActor
final class LockLineController {
    static let shared = LockLineController()

    private let writer = LockLineWriter()
    private weak var viewModel: UsageViewModel?
    private var timer: Timer?
    private var sessionActive = true

    func start(viewModel: UsageViewModel) {
        self.viewModel = viewModel
        let ws = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.sessionDidResignActiveNotification, NSWorkspace.screensDidSleepNotification,
                     NSWorkspace.sessionDidBecomeActiveNotification] {
            ws.addObserver(self, selector: #selector(sessionEvent(_:)), name: name, object: nil)
        }
        refresh()
        sweepHelper()
    }

    /// A helper registered while the switch is off (a reset, a crash mid-turn-off, defaults
    /// cleared by hand) is removed once it has taken the line away: the file is already empty
    /// or gone, so its next run clears the line or puts the earlier message back.
    private func sweepHelper() {
        guard !UserDefaults.desk.bool(forKey: LockLineKeys.on), LockLineDaemon.state != .off,
              LockLineDaemon.state != .missing else { return }
        let shown = LockLineDaemon.currentLoginText() ?? ""
        Task { @MainActor in await LockLineDaemon.remove(lastLine: shown) }
    }

    @objc private func sessionEvent(_ note: Notification) {
        if note.name == NSWorkspace.sessionDidBecomeActiveNotification {
            sessionActive = true
            refresh()
            return
        }
        // Fresh as the screen locks, then rest.
        refresh(force: true)
        if note.name == NSWorkspace.sessionDidResignActiveNotification { sessionActive = false; stopTimer() }
    }

    /// Settings changed, a tick fired or the session moved: compose and write if it changed.
    func refresh(force: Bool = false) {
        guard let vm = viewModel else { return }
        let d = UserDefaults.desk
        let on = d.bool(forKey: LockLineKeys.on)
        if !on { stopTimer() } else if sessionActive { startTimer() }
        switch LockLineWriter.plan(on: on, demo: vm.demo) {
        case .nothing: return
        case .clear: writer.clear(); return
        case .compose: break
        }
        guard sessionActive || force else { return }
        writer.write(Self.line(vm: vm, now: Date()))
    }

    /// The exact line as it would be written now; Settings' preview draws this too.
    static func line(vm: UsageViewModel, now: Date) -> String {
        let d = UserDefaults.desk
        let waiting = DeskController.shared.model.allWatchers.filter { $0.state == .waiting }.count
        let inputs = LockLineWriter.inputs(
            usage: vm.usage, lastUpdated: vm.lastUpdated, statusNormal: LockLineWriter.isNormal(vm.status),
            now: now, prefix: d.string(forKey: LockLineKeys.prefix) ?? "",
            show: LockLineKeys.slots(in: d), waitingCount: waiting)
        return LockLine.compose(inputs, now: now, calendar: .current, locale: .current)
    }

    private func startTimer() {
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: LockLineWriter.refreshInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
    }

    private func stopTimer() { timer?.invalidate(); timer = nil }
}
