import Foundation

/// What Codex's readings leave behind (agent layer spec, section 2; agent sources design, section 2),
/// the way Windows' `CodexUsageService` does it: its own history file, `history.@codex.json`, under
/// the reserved account `@codex` (a Claude label can't start with `@`, so the two never collide and
/// rename and remove never see it); its own snapshot, `snapshot.codex.json`, beside snapshot.json;
/// and alerts on its own once-keys, only for tiers that are fresh. Fed by `CodexUsageService`'s
/// `onUpdate` and `onStop`; every collaborator is injected so a test drives it over a temp folder.
/// Main thread only.
@MainActor
final class CodexRecorder {
    /// The history account Codex's points live under.
    static let historyAccount = "@codex"
    /// An unchanged number is not recorded again within this long (Windows' `HistoryEvery`).
    static let historyEvery: TimeInterval = 5 * 60

    private let files: HistoryStore.Files
    private let defaults: DefaultsStore
    private let snapshot: URL
    private let clock: () -> Date
    private let alert: (UsageResponse, Set<Tier>) -> Void
    private let forget: () -> Void

    /// Called with the sparklines after each reading: the recent window of `history.@codex.json`, read once
    /// and grown in memory like Claude's, so the widget never reads the file. Not called while Meter history
    /// is off for `@codex`.
    var onSparklines: ((HistoryStore.History) -> Void)?

    private var seeded = false
    private var sparklines: HistoryStore.History = [:]
    private var lastAppended: Date?
    private var lastPoint: [Tier: (pct: Int, at: Date)] = [:]
    private var lastSnapshotKey: String?
    private var lastStateKey: String?

    /// - Parameters:
    ///   - alert: evaluates Codex's alerts for a reading and the tiers that are fresh.
    ///   - forget: Codex stopped: the alert baseline goes, so the next reading is compared with nothing.
    init(files: HistoryStore.Files = .standard,
         defaults: DefaultsStore = KeychainStore.accounts.defaults,
         snapshot: URL = SnapshotWriter.codexURL,
         clock: @escaping () -> Date = Date.init,
         alert: @escaping (UsageResponse, Set<Tier>) -> Void = { Notifier.shared.evaluate($0, source: .codex, freshTiers: $1) },
         forget: @escaping () -> Void = { Notifier.shared.forget(.codex) }) {
        self.files = files
        self.defaults = defaults
        self.snapshot = snapshot
        self.clock = clock
        self.alert = alert
        self.forget = forget
    }

    /// One read: a reading to record, or nil when there are no limits (the snapshot goes, so a
    /// reader says "no Codex numbers" instead of showing old ones).
    func record(_ reading: CodexPublication?) {
        // Reset Sanduhr… ran: nothing comes back (the writers check too, but history writes run on
        // another queue).
        guard !ResetGate.isOn else { return }
        guard let reading else {
            lastSnapshotKey = nil
            lastStateKey = nil
            SnapshotWriter.delete(at: snapshot)
            return
        }
        appendHistory(reading)
        writeSnapshot(reading)
        let state = stateKey(reading)
        if state != lastStateKey {
            lastStateKey = state
            alert(reading.usage, reading.freshTiers)
        }
    }

    /// The service stopped. Its snapshot goes only when Codex itself is off: demo mode and Reset stop
    /// the service too, but a true reading from before is still true. The alert baseline always goes.
    func stopped(codexOn: Bool) {
        lastStateKey = nil
        forget()
        guard !codexOn else { return }
        lastSnapshotKey = nil
        SnapshotWriter.delete(at: snapshot)
    }

    // MARK: History

    private func appendHistory(_ r: CodexPublication) {
        guard MeterHistory.isOn(Self.historyAccount, in: defaults) else { return }
        if !seeded {
            seeded = true
            let onFile = HistoryStore.io.sync { files.load(Self.historyAccount) }
            lastAppended = Self.newest(onFile)
            sparklines = HistoryStore.sparklines(onFile)
            onSparklines?(sparklines)
        }
        // The same event again (a restart, a poll with nothing new) is already on file.
        if let last = lastAppended, r.asOf <= last { return }
        var readings: [(Tier, Double)] = []
        for tier in Tier.allCases {
            guard let pct = r.usage.tiers[tier]?.utilization else { continue }
            let rounded = Int(pct.rounded())
            if let prior = lastPoint[tier], prior.pct == rounded, r.asOf.timeIntervalSince(prior.at) < Self.historyEvery { continue }
            readings.append((tier, Double(rounded)))
            lastPoint[tier] = (rounded, r.asOf)
        }
        let now = clock()
        if HistoryStore.record(readings, account: Self.historyAccount, defaults: defaults, files: files, now: now) {
            sparklines = HistoryStore.sparklines(sparklines, adding: readings, at: now)
            onSparklines?(sparklines)
        }
        lastAppended = r.asOf
    }

    private static func newest(_ history: HistoryStore.History) -> Date? {
        history.values.flatMap { $0 }.compactMap { HistoryStore.date($0.t) }.max()
    }

    // MARK: Snapshot

    /// Stamped with the event's time, so a reader sees "Codex idle", not "Sanduhr stopped". Written
    /// again only when the reading changed.
    private func writeSnapshot(_ r: CodexPublication) {
        let key = "\(r.asOf.timeIntervalSince1970)|\(r.planType ?? "")|\(tiersKey(r.usage))"
        guard key != lastSnapshotKey else { return }
        SnapshotWriter.writeOk(r.usage, accountRef: nil, plan: r.planType, now: r.asOf, source: "codex", to: snapshot)
        lastSnapshotKey = key
    }

    private func stateKey(_ r: CodexPublication) -> String {
        let fresh = r.freshTiers.map(\.rawValue).sorted().joined(separator: ",")
        return "\(r.asOf.timeIntervalSince1970)|\(fresh)|\(tiersKey(r.usage))"
    }

    private func tiersKey(_ u: UsageResponse) -> String {
        Tier.allCases.compactMap { tier in
            u.tiers[tier].map { "\(tier.rawValue)=\($0.utilization ?? -1)@\($0.resetsAt ?? "")" }
        }.joined(separator: ";")
    }
}
