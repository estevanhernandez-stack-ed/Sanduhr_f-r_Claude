import Foundation
import SwiftUI
import Combine
import LocalAuthentication

/// Central state for the widget. Owns the API client, refresh/countdown
/// timers, theme + compact/pin preferences, and the last-fetched `UsageResponse`.
@Observable
@MainActor
final class UsageViewModel {
    // MARK: Config (persisted)

    var theme: Theme {
        didSet { UserDefaults.standard.set(theme.id, forKey: "theme") }
    }

    var compact: Bool = false              // double-click title to toggle
    var pinned: Bool = true                // floats on top when true

    // MARK: Runtime state

    var usage: UsageResponse?
    var lastUpdated: Date?
    var status: StatusMessage = .connecting
    var history: HistoryStore.History = HistoryStore.load()
    /// Bumped every 30s so countdown labels re-render without refetching.
    var countdownTick: Int = 0
    /// Set to `true` externally (e.g. from the menu bar) to pop the
    /// credentials sheet on the main widget. RootView flips it back to
    /// false once it consumes the signal.
    var requestSettingsSheet: Bool = false

    /// Called after each successful refresh (and whenever `status` flips
    /// meaningfully). The menu bar status item uses this to re-render its
    /// title. Kept as a plain closure to avoid dragging AppKit into the
    /// view model's import list.
    var onUsageUpdate: (() -> Void)?

    /// The tier with the highest utilization, or nil if we have nothing.
    /// Used by the menu bar status item to show a single at-a-glance %.
    func highestTier() -> (tier: Tier, usage: TierUsage)? {
        guard let u = usage else { return nil }
        return Tier.allCases.compactMap { t -> (Tier, TierUsage)? in
            guard let tu = u.tiers[t], tu.utilization != nil else { return nil }
            return (t, tu)
        }.max { ($0.1.utilization ?? 0) < ($1.1.utilization ?? 0) }
    }

    enum StatusMessage: Equatable {
        case connecting
        case refreshing
        case idle
        case noTiers
        case error(String, isAuth: Bool)

        var text: String {
            switch self {
            case .connecting:           return "Connecting…"
            case .refreshing:           return "Refreshing…"
            case .idle, .noTiers:       return ""
            case .error(let m, _):      return m
            }
        }
        var isError: Bool {
            if case .error = self { return true }
            return false
        }
    }

    // MARK: Private

    private var api: ClaudeAPI?
    private var refreshTimer: Timer?
    private var countdownTimer: Timer?
    /// One context for all Keychain reads this launch — .userPresence
    /// ACL respects `touchIDAuthenticationAllowableReuseDuration`, so
    /// reusing the same context means one Touch ID tap per session.
    private let authContext: LAContext = {
        let c = LAContext()
        c.touchIDAuthenticationAllowableReuseDuration =
            LATouchIDAuthenticationMaximumAllowableReuseDuration
        return c
    }()

    /// 5 min between API calls, mirroring sanduhr.py:37.
    private let refreshInterval: TimeInterval = 5 * 60
    /// 30 s between countdown UI updates, mirroring sanduhr.py:38.
    private let countdownInterval: TimeInterval = 30

    // MARK: Init

    init() {
        let stored = UserDefaults.standard.string(forKey: "theme") ?? ""
        self.theme = ThemeRegistry.theme(id: stored) ?? ThemeRegistry.default
    }

    /// Called once the app has a window + a session key.
    /// Uses `exists()` first (no Touch ID) to decide whether to even try
    /// reading — skips an unnecessary prompt on first launch.
    func bootstrap() {
        guard KeychainStore.exists(account: KeychainAccount.sessionKey) else {
            status = .connecting     // onboarding sheet will drive the next step
            return
        }
        // One Touch ID prompt here unlocks both reads thanks to shared context.
        if let key = KeychainStore.get(account: KeychainAccount.sessionKey,
                                       context: authContext),
           !key.isEmpty {
            let cf = KeychainStore.get(account: KeychainAccount.cfClearance,
                                       context: authContext)
            api = ClaudeAPI(sessionKey: key, cfClearance: cf)
            Task { await refresh() }
            startTimers()
        } else {
            status = .connecting
        }
    }

    /// Called after the user saves new credentials. Reuses the authContext
    /// so saving + immediate refresh doesn't require another Touch ID tap
    /// (the 10-second reuse window covers the round trip).
    func credentialsChanged() {
        guard let key = KeychainStore.get(account: KeychainAccount.sessionKey,
                                          context: authContext),
              !key.isEmpty else { return }
        let cf = KeychainStore.get(account: KeychainAccount.cfClearance,
                                   context: authContext)
        api = ClaudeAPI(sessionKey: key, cfClearance: cf)
        Task { await refresh() }
        startTimers()
    }

    // MARK: Timers

    private func startTimers() {
        refreshTimer?.invalidate()
        countdownTimer?.invalidate()

        refreshTimer = Timer.scheduledTimer(withTimeInterval: refreshInterval,
                                            repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.refresh() }
        }
        countdownTimer = Timer.scheduledTimer(withTimeInterval: countdownInterval,
                                              repeats: true) { [weak self] _ in
            Task { @MainActor in self?.countdownTick &+= 1 }
        }
    }

    // MARK: Refresh

    func refresh() async {
        guard let api else { return }
        status = .refreshing
        onUsageUpdate?()
        defer { onUsageUpdate?() }
        do {
            let u = try await api.getUsage()
            self.usage = u
            self.lastUpdated = Date()
            for (tier, t) in u.tiers {
                if let util = t.utilization {
                    HistoryStore.append(tier, utilization: util)
                }
            }
            self.history = HistoryStore.load()
            self.status = u.tiers.isEmpty ? .noTiers : .idle
        } catch ClaudeAPI.APIError.unauthorized {
            self.status = .error("Session expired — click Key", isAuth: true)
        } catch ClaudeAPI.APIError.cloudflareChallenge {
            self.status = .error("Cloudflare — add cf_clearance", isAuth: true)
        } catch let ClaudeAPI.APIError.http(c) {
            self.status = .error("HTTP \(c)", isAuth: false)
        } catch {
            self.status = .error(error.localizedDescription, isAuth: false)
        }
    }

    // MARK: UI helpers

    /// Returns tiers the server reported (with a non-nil utilization), in
    /// display order. If compact mode is on, returns only the highest one.
    /// Mirrors sanduhr.py:468-478.
    func visibleTiers() -> [(tier: Tier, usage: TierUsage)] {
        guard let u = usage else { return [] }
        let active = Tier.allCases.compactMap { t -> (Tier, TierUsage)? in
            guard let tu = u.tiers[t], tu.utilization != nil else { return nil }
            return (t, tu)
        }
        if compact, let top = active.max(by: {
            ($0.1.utilization ?? 0) < ($1.1.utilization ?? 0)
        }) {
            return [top]
        }
        return active
    }

    func footerText() -> String {
        guard let ts = lastUpdated else { return "" }
        let f = DateFormatter(); f.dateFormat = "h:mm a"
        let mode = compact ? "Compact" : (pinned ? "Pinned" : "Float")
        return "Updated \(f.string(from: ts)) · \(mode)"
    }
}
