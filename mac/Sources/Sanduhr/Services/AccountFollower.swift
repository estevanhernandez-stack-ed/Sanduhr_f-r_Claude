import Foundation

/// The scheduling shell around `AccountFollow.decide`: every 15 minutes, with the setting on,
/// each signed-in inactive account gets one usage request through its own client, and the
/// decision runs on the readings. Kept thin; the rules live in AccountFollow.
///
/// - Each account's client is kept between checks, so its org id is cached per account and only
///   its first check asks for `/organizations`. A new key (or cf_clearance) builds a new client.
/// - A failed check is ignored and never shown. An auth error (session expired, Cloudflare)
///   skips that account until a different key is saved for it.
/// - Nothing here logs; labels never leave memory.
@MainActor
final class AccountFollower {
    /// The setting in the app's defaults, off by default for new installs and upgrades.
    static let enabledKey = "followAccount"

    /// Readings, the last automatic switch and the pause. The view model fills in the rest of
    /// the state (active, signed in, switching) when it asks for a decision.
    private(set) var state = AccountFollow.State()

    /// The view model's part of the state, read at decision time.
    var context: () -> AccountFollow.State = { AccountFollow.State() }
    /// The decision said switch.
    var onSwitch: (String) -> Void = { _ in }
    /// New readings: the "in use" marks may have changed.
    var onReadings: () -> Void = {}

    private var timer: Timer?
    private var firstCheck: Task<Void, Never>?
    private var checking = false
    private var clients: [String: (credentials: AccountCredentials, api: ClaudeAPI)] = [:]
    /// Accounts skipped after an auth error, with the key that failed.
    private var authSkipped: [String: String] = [:]

    // MARK: Lifecycle

    /// Starts or stops the checks to match the setting. The first check runs a minute after
    /// starting (it is the baseline the next one is compared with), then every 15 minutes.
    func apply(enabled: Bool) {
        timer?.invalidate()
        timer = nil
        firstCheck?.cancel()
        firstCheck = nil
        guard enabled else {
            clients = [:]
            return
        }
        let t = Timer(timeInterval: AccountFollow.checkInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.check() }
        }
        t.tolerance = 60
        RunLoop.main.add(t, forMode: .common)
        timer = t
        firstCheck = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 60 * 1_000_000_000)
            guard !Task.isCancelled else { return }
            await self?.check()
        }
    }

    // MARK: Inputs

    /// A good fetch of the active account (its normal 5-minute cadence).
    func recordActive(_ label: String?, usage: UsageResponse, now: Date = Date()) {
        guard let label, let util = usage.tiers[.fiveHour]?.utilization else { return }
        state.record(label, utilization: util, at: now)
        onReadings()
    }

    /// A manual switch (chip, menu, Settings) pauses following.
    func manualSwitch(to label: String, now: Date = Date()) {
        state.pause = AccountFollow.Pause(account: label, since: now)
    }

    func automaticSwitch(now: Date = Date()) {
        state.lastAutoSwitch = now
    }

    func rename(_ old: String, to new: String) {
        state.rename(old, to: new)
        if let c = clients.removeValue(forKey: old) { clients[new] = c }
        if let k = authSkipped.removeValue(forKey: old) { authSkipped[new] = k }
    }

    /// Signed out or removed: its client, its readings and its skip go.
    func forget(_ label: String) {
        state.forget(label)
        clients.removeValue(forKey: label)
        authSkipped.removeValue(forKey: label)
    }

    /// The decision state as of now: the readings here, the rest from the view model.
    func current() -> AccountFollow.State {
        var s = context()
        s.readings = state.readings
        s.lastAutoSwitch = state.lastAutoSwitch
        s.pause = state.pause
        return s
    }

    // MARK: The check

    /// One round: a usage request per signed-in inactive account, then the decision.
    func check(now: () -> Date = Date.init) async {
        guard !checking else { return }
        let s = current()
        guard s.enabled, let active = s.active, !s.activeSignedOut, !s.switching,
              s.signedIn.count > 1 else { return }
        checking = true
        defer { checking = false }
        for label in s.signedIn.sorted() where label != active {
            await checkOne(label, now: now)
        }
        onReadings()
        if case .switchTo(let label) = AccountFollow.decide(current(), now: now()) {
            onSwitch(label)
        }
    }

    private func checkOne(_ label: String, now: () -> Date) async {
        let creds = KeychainStore.accounts.credentials(for: label)
        guard let key = creds.sessionKey else { return }
        if let failed = authSkipped[label] {
            guard failed != key else { return }
            authSkipped.removeValue(forKey: label)
        }
        let api: ClaudeAPI
        if let c = clients[label], c.credentials == creds {
            api = c.api
        } else {
            api = ClaudeAPI(sessionKey: key, cfClearance: creds.cfClearance)
            clients[label] = (creds, api)
        }
        do {
            let usage = try await api.getUsage()
            // Renamed, signed out or removed while the request ran: the answer belongs to no one.
            guard clients[label]?.api === api,
                  let util = usage.tiers[.fiveHour]?.utilization else { return }
            state.record(label, utilization: util, at: now())
        } catch ClaudeAPI.APIError.unauthorized, ClaudeAPI.APIError.cloudflareChallenge {
            if clients[label]?.api === api { authSkipped[label] = key }
        } catch {
            // Failed checks are silent.
        }
    }
}
