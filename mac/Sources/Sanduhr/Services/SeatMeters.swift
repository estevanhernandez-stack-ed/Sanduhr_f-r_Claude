import Foundation

/// What a seat fetch asks of a client: `ClaudeAPI`, or a test's fake.
protocol UsageFetching: AnyObject, Sendable {
    func getUsage() async throws -> UsageResponse
}

extension ClaudeAPI: UsageFetching {}

/// Per-seat meters (item 74), the stateful half: keeps the files of `SeatSnapshots` in line with
/// the accounts and fetches the inactive linked accounts by the fetch rule. The view model hands it
/// its state through `context`; clients, the clock and the wait between fetches are injectable.
///
/// The fetch rule: after each active fetch that did not fail on the network, every inactive
/// signed-in linked account in use is fetched in turn, `SeatSnapshots.stagger` apart, one round at
/// a time, through its own client (its org id cached). In use: following saw it in use, or Claude
/// Code wrote a session log in its folder in the last 10 minutes (`SeatActivity`, file times only,
/// checked once per round; following itself never hears of it). An account that becomes a seat
/// (linked, signed in, at launch, or no longer the active one) is also fetched once soon after when
/// its file is missing or older than the interval, so a seat shows a reading with Follow off. An account read within
/// the refresh interval (by a round or by following's own check) is skipped, so a manual refresh
/// never fetches it again early. A refused key skips it until a different key is saved. An answer
/// is written only while the account is still linked, signed in and not active.
@MainActor
final class SeatMeters {
    /// The view model's state, read at each step.
    struct Context: Equatable {
        var labels: [String] = []
        var choices: [String: AccountDataChoices] = [:]
        var signedIn: Set<String> = []
        var active: String?
        var inUse: Set<String> = []

        var linked: [String: String] { SeatSnapshots.linked(choices: choices, labels: labels) }
    }

    let files: SeatSnapshots.Files
    /// The active account's refresh interval: the least time between two reads of an account.
    let interval: TimeInterval
    var context: () -> Context = { Context() }
    var credentials: (String) -> AccountCredentials = { KeychainStore.accounts.credentials(for: $0) }
    var makeClient: (AccountCredentials) -> UsageFetching = {
        ClaudeAPI(sessionKey: $0.sessionKey ?? "", cfClearance: $0.cfClearance)
    }
    var now: () -> Date = Date.init
    var sleep: (TimeInterval) async -> Void = { try? await Task.sleep(nanoseconds: UInt64($0 * 1_000_000_000)) }
    /// Whether Claude Code wrote to a seat folder lately. Runs off the main thread.
    var recentActivity: @Sendable (String) -> Bool = { folder in
        SeatActivity.recent(folder: folder, fs: LocalCCLogFileSystem(), now: Date())
    }

    /// When each account was last read for its seat (a round's request, or following's check).
    private(set) var lastFetch: [String: Date] = [:]
    private var clients: [String: (credentials: AccountCredentials, client: UsageFetching)] = [:]
    private var authSkipped: [String: String] = [:]
    private(set) var round: Task<Void, Never>?
    private var synced: (seats: [SeatSnapshots.Seat], signedOut: Set<String>)?
    /// The accounts a seat fetch may write, as of the last sync: one new here is fetched once.
    private var knownFetchable: Set<String> = []
    /// Accounts waiting for that one fetch.
    private(set) var pending: Set<String> = []

    init(files: SeatSnapshots.Files = .standard, interval: TimeInterval = 5 * 60) {
        self.files = files
        self.interval = interval
    }

    /// The file a reading of `label` goes to, nil when the account has no folder linked.
    func target(_ label: String?) -> URL? {
        guard let label, context().linked[label] != nil, let ref = AccountRef.of(label) else { return nil }
        return files.snapshot(ref)
    }

    // MARK: Files

    /// seats.json and the per-account files follow every change to the accounts, their sign-in
    /// and their links. Cheap when nothing changed.
    func sync() {
        let c = context()
        let linked = c.linked
        let seats = SeatSnapshots.seats(linked)
        let signedOut = Set(linked.keys.filter { !c.signedIn.contains($0) }.compactMap(AccountRef.of))
        if synced == nil || synced?.seats != seats || synced?.signedOut != signedOut {
            synced = (seats, signedOut)
            let keep = Set(linked.keys)
            clients = clients.filter { keep.contains($0.key) && c.signedIn.contains($0.key) }
            authSkipped = authSkipped.filter { keep.contains($0.key) }
            lastFetch = lastFetch.filter { keep.contains($0.key) && c.signedIn.contains($0.key) }
            SeatSnapshots.sync(seats, signedOut: signedOut, in: files, now: now())
        }
        // A new seat with no reading, or an old one, is fetched once soon.
        let fetchable = Set(linked.keys.filter { SeatSnapshots.fetchable($0, linked: linked, signedIn: c.signedIn, active: c.active) })
        for label in fetchable.subtracting(knownFetchable) where needsReading(label) { pending.insert(label) }
        knownFetchable = fetchable
        pending.formIntersection(fetchable)
        if !pending.isEmpty { startRound() }
    }

    /// The seat's file is missing or older than the interval.
    private func needsReading(_ label: String) -> Bool {
        guard let url = target(label), let at = SeatSnapshots.capturedAt(url) else { return true }
        return now().timeIntervalSince(at) >= interval
    }

    /// The active account's seat starts from the numbers on screen, stamped with their fetch time,
    /// rather than waiting for the next fetch.
    func seed(_ usage: UsageResponse?, at: Date?, label: String?) {
        guard let usage, let at, let target = target(label),
              !FileManager.default.fileExists(atPath: target.path) else { return }
        SnapshotWriter.writeOk(usage, accountRef: AccountRef.of(label), now: at, to: target)
    }

    /// A renamed account: its seat file moves to the new ref, its client and times follow.
    func rename(_ old: String, to new: String) {
        if let o = AccountRef.of(old), let n = AccountRef.of(new) { SeatSnapshots.rename(o, to: n, in: files) }
        if let v = clients.removeValue(forKey: old) { clients[new] = v }
        if let v = authSkipped.removeValue(forKey: old) { authSkipped[new] = v }
        if let v = lastFetch.removeValue(forKey: old) { lastFetch[new] = v }
        synced = nil
    }

    // MARK: Answers

    /// The active account's fetch answered: its seat file gets the same, then a round runs unless
    /// the fetch failed on the network (claude.ai unreachable or limiting: nothing more is asked).
    func activeAnswered(_ label: String?, _ result: Result<UsageResponse, Error>) {
        write(label, result)
        if case .failure(let e) = result, SnapshotWriter.errorKind(e) == "network" { return }
        startRound()
    }

    /// Following's own check of an inactive account answered (no request made for this).
    func followerAnswered(_ label: String, _ result: Result<UsageResponse, Error>) {
        let c = context()
        guard SeatSnapshots.fetchable(label, linked: c.linked, signedIn: c.signedIn, active: c.active) else { return }
        lastFetch[label] = now()
        write(label, result)
    }

    private func write(_ label: String?, _ result: Result<UsageResponse, Error>) {
        guard let target = target(label) else { return }
        let ref = AccountRef.of(label)
        switch result {
        case .success(let u): SnapshotWriter.writeOk(u, accountRef: ref, now: now(), to: target)
        case .failure(let e): SnapshotWriter.writeError(SnapshotWriter.errorKind(e), accountRef: ref, now: now(), to: target)
        }
    }

    // MARK: Rounds

    private func throttled(_ label: String) -> Bool {
        // A tenth of the interval absorbs the timer's tolerance and the requests' own time.
        guard let last = lastFetch[label] else { return false }
        return now().timeIntervalSince(last) < interval * 0.9
    }

    /// One round: the due accounts in turn, `stagger` apart. Nil when a round is running or no
    /// seat could be due. Due: waiting for its first fetch, in use for following, or, checked once
    /// here off the main thread, with Claude Code activity in its folder.
    @discardableResult
    func startRound() -> Task<Void, Never>? {
        guard round == nil else { return nil }
        let c = context()
        let candidates = c.linked.keys.filter {
            SeatSnapshots.fetchable($0, linked: c.linked, signedIn: c.signedIn, active: c.active) && !throttled($0)
        }.sorted()
        guard !candidates.isEmpty else { return nil }
        let task = Task { [weak self] in
            guard let self else { return }
            var due = candidates.filter { c.inUse.contains($0) || self.pending.contains($0) }
            let rest = candidates.filter { !due.contains($0) }.compactMap { l in c.linked[l].map { (l, $0) } }
            if !rest.isEmpty {
                let check = self.recentActivity
                due += await Task.detached(priority: .utility) { rest.filter { check($0.1) }.map(\.0) }.value
            }
            self.pending.subtract(due)
            for label in due.sorted() {
                await self.sleep(SeatSnapshots.stagger)
                await self.fetch(label)
            }
            self.round = nil
            // A seat that became new during the round gets its turn now.
            if !self.pending.isEmpty { self.startRound() }
        }
        round = task
        return task
    }

    private func fetch(_ label: String) async {
        let c = context()
        guard SeatSnapshots.fetchable(label, linked: c.linked, signedIn: c.signedIn, active: c.active),
              !throttled(label) else { return }
        let creds = credentials(label)
        guard let key = creds.sessionKey, !key.isEmpty else { return }
        if let failed = authSkipped[label] {
            guard failed != key else { return }
            authSkipped.removeValue(forKey: label)
        }
        let client: UsageFetching
        if let held = clients[label], held.credentials == creds {
            client = held.client
        } else {
            client = makeClient(creds)
            clients[label] = (creds, client)
        }
        lastFetch[label] = now()
        let result: Result<UsageResponse, Error>
        do { result = .success(try await client.getUsage()) } catch { result = .failure(error) }
        // Switched to, signed out, removed, unlinked or renamed while the request ran: no one's.
        let after = context()
        guard clients[label]?.client === client,
              SeatSnapshots.fetchable(label, linked: after.linked, signedIn: after.signedIn, active: after.active)
        else { return }
        if case .failure(let e) = result, SnapshotWriter.errorKind(e) != "network" { authSkipped[label] = key }
        write(label, result)
    }
}

/// Whether Claude Code is working in a seat folder (item 74): the newest session log under
/// `<folder>/projects` was modified within `window`. File times only: no log is opened.
enum SeatActivity {
    static let window: TimeInterval = 10 * 60

    static func recent(folder: String, fs: CCLogFileSystem, now: Date, within: TimeInterval = window) -> Bool {
        let projects = (folder as NSString).appendingPathComponent("projects")
        guard fs.isDirectory(projects) else { return false }
        return fs.jsonlFiles(under: projects).contains { file in
            fs.stat(file).map { now.timeIntervalSince($0.modified) <= within } ?? false
        }
    }
}
