import Foundation

/// A recorded folder's vault directory name: the first 16 hex digits (64 bits) of the SHA-256
/// of its standardized absolute path, lowercased (Mac volumes are case-insensitive, the same
/// fold as the checkpoint keys). Stable for a folder, the same on every launch, and not
/// reversible to the path; never the account label. `mcp-access.json` (item 47) names folders
/// by this id.
enum VaultFolderID {
    static func of(_ folder: String) -> String {
        String(VaultStore.sha256Hex(AccountData.normalized(folder).lowercased()).prefix(16))
    }
}

/// What state.yaml shows for the active account's record: counts and flags, never a path, a
/// label, a project or an id.
struct VaultState: Equatable, Sendable {
    var recording = false
    var months = 0
    var lastIngestOK = false
}

/// The app's owner of the vault (item 46), Windows `VaultService` per account: which folders
/// are recorded, the ingest after launch and every refresh, and erase.
///
/// **Who is recorded.** Every account whose Claude Code activity is Keep a record and that has
/// a folder linked, not only the active one: reading logs is local and cheap with checkpoints,
/// and a record should keep up for every account that asked for one. Live only and Not tracked
/// never write.
///
/// **Ingest.** Off the main thread on a utility queue, one cycle at a time in this process (a
/// request during a cycle is dropped: the next refresh asks again), one writer across processes
/// (`VaultWriterLock`; a second Sanduhr skips its cycle).
///
/// **Erase**, in this order: the account's activity leaves Keep a record first (the tombstone:
/// the consent state, not the folder, is what keeps a record from coming back); then the writer
/// lock is taken, waiting up to 10 seconds for an in-flight cycle, which notices the change
/// before each file and before its writes and stops without writing; then the folder's vault
/// directory is deleted. A holder that never lets go doesn't block the erase: it deletes anyway
/// and deletes again once it gets the lock.
final class VaultService: @unchecked Sendable {
    static let eraseWait: TimeInterval = 10

    let store: VaultStore
    let reader: VaultReader
    private let ingester: VaultIngester
    private let defaults: () -> DefaultsStore
    private let lockPath: String
    private let queue = DispatchQueue(label: "com.626labs.sanduhr.vault", qos: .utility)
    private let eraseQueue = DispatchQueue(label: "com.626labs.sanduhr.vault-erase", qos: .userInitiated)
    private let lock = NSLock()
    private var running = false
    private var lastOK: [String: Bool] = [:]
    private var _onCycleEnd: (@Sendable () -> Void)?

    init(vaultDir: String, defaults: @escaping () -> DefaultsStore, writerVersion: String,
         timeZone: TimeZone = .current, log: VaultLogSink? = VaultLog.system,
         dirHoldsGit: ((String) -> Bool)? = nil) {
        store = VaultStore(vaultDir: vaultDir, log: log)
        reader = VaultReader(store: store)
        lockPath = (vaultDir as NSString).appendingPathComponent(".writer.lock")
        ingester = VaultIngester(store: store, writerVersion: writerVersion, lockPath: lockPath,
                                 timeZone: timeZone, log: log, dirHoldsGit: dirHoldsGit)
        self.defaults = defaults
    }

    /// `~/Library/Application Support/Sanduhr/vault`, the registry's defaults.
    static let standard: VaultService = {
        let dir = HistoryStore.Files.standard.dir.appendingPathComponent("vault", isDirectory: true).path
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
        return VaultService(vaultDir: dir, defaults: { KeychainStore.accounts.defaults }, writerVersion: version)
    }()

    // MARK: Who is recorded

    /// The folders to record: accounts with Keep a record and a linked folder, one entry per
    /// folder, each with its account's project-names choice.
    static func roots(in d: DefaultsStore) -> [VaultRoot] {
        var seen = Set<String>()
        var out: [VaultRoot] = []
        for (_, c) in AccountData.allChoices(in: d).sorted(by: { $0.key < $1.key }) {
            guard c.activity == .record, let folder = c.folder, !folder.isEmpty else { continue }
            let id = VaultFolderID.of(folder)
            guard seen.insert(id).inserted else { continue }
            out.append(VaultRoot(folder: AccountData.normalized(folder), id: id, names: c.names))
        }
        return out
    }

    /// Whether some account still keeps a record of the folder with this id.
    static func isRecording(_ id: String, in d: DefaultsStore) -> Bool {
        roots(in: d).contains { $0.id == id }
    }

    // MARK: Ingest

    /// Starts a cycle off the main thread unless one is running or nothing is recorded.
    func trigger() {
        let d = defaults()
        let roots = Self.roots(in: d)
        guard !roots.isEmpty else { return }
        lock.lock()
        guard !running else {
            lock.unlock()
            return
        }
        running = true
        lock.unlock()
        queue.async { [self] in
            _ = ingestNow(roots)
            lock.lock()
            running = false
            let done = onCycleEnd
            lock.unlock()
            done?()
        }
    }

    /// Called after each triggered cycle, on the vault's queue (the Claude Usage page reloads).
    var onCycleEnd: (@Sendable () -> Void)? {
        get { lock.lock(); defer { lock.unlock() }; return _onCycleEnd }
        set { lock.lock(); _onCycleEnd = newValue; lock.unlock() }
    }

    /// One cycle, on the caller's thread: each folder in turn, every write rechecked against
    /// the current choices. Returns the counts summed.
    @discardableResult
    func ingestNow(_ roots: [VaultRoot]? = nil, now: Date = Date()) -> VaultIngestResult {
        let d = defaults()
        let list = roots ?? Self.roots(in: d)
        var total = VaultIngestResult()
        for root in list {
            let recording: (String) -> Bool = { Self.isRecording($0, in: self.defaults()) }
            let r = ingester.ingestOnce([root], now: now, stillRecording: recording,
                                        shouldStop: { !recording($0) })
            guard r.acquired else {
                total.acquired = false
                break
            }
            total.add(r)
            if r.rootsWithdrawn == 0 {
                lock.lock()
                lastOK[root.id] = r.rootsAborted == 0
                lock.unlock()
            }
        }
        return total
    }

    // MARK: Erase

    /// Deletes the record of `folder`, once no account records it any more (the caller turns
    /// Keep a record off first, or removed the account). Runs off the main thread; `done` gets
    /// true when the directory is gone, false when the folder is being recorded again.
    func erase(folder: String, done: (@Sendable (Bool) -> Void)? = nil) {
        eraseQueue.async { [self] in
            let ok = eraseNow(folder: folder)
            done?(ok)
        }
    }

    /// `erase` on the caller's thread.
    @discardableResult
    func eraseNow(folder: String, wait: TimeInterval = VaultService.eraseWait) -> Bool {
        eraseNow(id: VaultFolderID.of(folder), wait: wait)
    }

    /// Deletes the record with this vault id: the stewardship list's Erase (item 48), which also
    /// reaches records whose folder no account links any more. The same sequence as `erase`:
    /// nothing while an account still records it.
    func erase(id: String, done: (@Sendable (Bool) -> Void)? = nil) {
        eraseQueue.async { [self] in
            let ok = eraseNow(id: id)
            done?(ok)
        }
    }

    /// `erase(id:)` on the caller's thread.
    @discardableResult
    func eraseNow(id: String, wait: TimeInterval = VaultService.eraseWait) -> Bool {
        guard VaultStewardship.isRecordID(id) else { return false }
        guard !Self.isRecording(id, in: defaults()) else { return false }
        let held = VaultWriterLock.acquire(lockPath, timeout: wait)
        guard !Self.isRecording(id, in: defaults()) else {
            held?.release()
            return false
        }
        let gone = store.purgeRoot(id)
        lock.lock()
        lastOK.removeValue(forKey: id)
        lock.unlock()
        if let held {
            held.release()
        } else {
            // The holder outlasted the wait. It rechecks before writing, so the folder stays
            // gone; delete again once the lock comes free in case it had already begun writing.
            eraseQueue.async { [self] in
                guard let late = VaultWriterLock.acquire(lockPath, timeout: 600) else { return }
                if !Self.isRecording(id, in: defaults()) { store.purgeRoot(id) }
                late.release()
            }
        }
        return gone
    }

    // MARK: State

    /// Every record on this Mac, linked or not (item 48's stewardship list).
    func records(_ choices: [String: AccountDataChoices]) -> [VaultRecordInfo] {
        VaultStewardship.records(in: store, choices: choices)
    }

    /// Session-shard months kept for the folder.
    func months(folder: String) -> Int {
        store.listSessionShardMonths(VaultFolderID.of(folder)).count
    }

    func hasRecord(folder: String) -> Bool {
        store.rootExists(VaultFolderID.of(folder))
    }

    /// state.yaml's `vault:` for an account's choices.
    func state(for c: AccountDataChoices) -> VaultState {
        guard let folder = c.folder, !folder.isEmpty else { return VaultState() }
        let id = VaultFolderID.of(folder)
        lock.lock()
        let ok = lastOK[id] ?? false
        lock.unlock()
        return VaultState(recording: c.activity == .record, months: months(folder: folder), lastIngestOK: ok)
    }
}
