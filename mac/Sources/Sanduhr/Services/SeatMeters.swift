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
/// signed-in linked account following sees in use is fetched in turn, `SeatSnapshots.stagger`
/// apart, one round at a time, through its own client (its org id cached). An account read within
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

    /// When each account was last read for its seat (a round's request, or following's check).
    private(set) var lastFetch: [String: Date] = [:]
    private var clients: [String: (credentials: AccountCredentials, client: UsageFetching)] = [:]
    private var authSkipped: [String: String] = [:]
    private(set) var round: Task<Void, Never>?
    private var synced: (seats: [SeatSnapshots.Seat], signedOut: Set<String>)?

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
        if let synced, synced.seats == seats, synced.signedOut == signedOut { return }
        synced = (seats, signedOut)
        let keep = Set(linked.keys)
        clients = clients.filter { keep.contains($0.key) && c.signedIn.contains($0.key) }
        authSkipped = authSkipped.filter { keep.contains($0.key) }
        lastFetch = lastFetch.filter { keep.contains($0.key) && c.signedIn.contains($0.key) }
        SeatSnapshots.sync(seats, signedOut: signedOut, in: files, now: now())
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

    /// One round: the due accounts in turn, `stagger` apart. Nil when a round is running or none is due.
    @discardableResult
    func startRound() -> Task<Void, Never>? {
        guard round == nil else { return nil }
        let c = context()
        let due = SeatSnapshots.due(linked: c.linked, signedIn: c.signedIn, active: c.active, inUse: c.inUse)
            .filter { !throttled($0) }
        guard !due.isEmpty else { return nil }
        let task = Task { [weak self] in
            for label in due {
                guard let self else { return }
                await self.sleep(SeatSnapshots.stagger)
                await self.fetch(label)
            }
            self?.round = nil
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
