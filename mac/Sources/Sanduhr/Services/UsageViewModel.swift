import Foundation
import SwiftUI
import Combine

extension Notification.Name {
    /// Posted by `UsageViewModel` whenever `compact` flips. AppDelegate
    /// listens so it can shrink/grow the NSPanel to match the new content
    /// height — SwiftUI alone can't drive the underlying window size.
    static let sanduhrCompactDidChange = Notification.Name("sanduhr.compactDidChange")
}

/// Central state for the widget. Owns the API client, refresh/countdown
/// timers, theme + compact/pin preferences, and the last-fetched `UsageResponse`.
@Observable
@MainActor
final class UsageViewModel {
    // MARK: Config (persisted)

    var theme: Theme {
        didSet {
            UserDefaults.standard.set(theme.id, forKey: "theme")
            applyDeskLook()
        }
    }

    /// The one way a theme is picked: the widget's Theme menu and the Settings gallery both
    /// call it, so they always agree. Takes the registry's current copy of the theme; Match
    /// Desk is built from Desk's settings as they are now.
    func selectTheme(id: String) {
        if let picked = Self.resolve(id) { theme = picked }
    }

    /// A theme by id as the widget draws it: the registry's copy, or for the built-in Match
    /// Desk, the theme drawn in Desk's current ink.
    nonisolated static func resolve(_ id: String, desk: DefaultsStore = UserDefaults.desk) -> Theme? {
        guard let registered = ThemeRegistry.theme(id: id) else { return nil }
        guard registered.palette.ink != nil else { return registered }
        let look = DeskLook.read(desk)
        return DeskThemeMapping.theme(ink: look.ink, shadow: look.shadow)
    }

    /// Re-reads the current theme by id, so a user theme edited and reloaded, or Desk's ink
    /// changed under Match Desk, restyles the widget without being picked again. A theme that
    /// no longer resolves stays as it is.
    func reresolveTheme() {
        if let fresh = Self.reresolved(theme) { theme = fresh }
        applyDeskLook()
    }

    /// The current theme as the registry (or Desk, for Match Desk) has it now, or nil when it
    /// is unchanged or no longer resolves.
    nonisolated static func reresolved(_ current: Theme, desk: DefaultsStore = UserDefaults.desk) -> Theme? {
        guard let fresh = resolve(current.id, desk: desk), fresh != current else { return nil }
        return fresh
    }

    /// Match Desk's font and glass-free drawing while it is current; the user's own font and
    /// subtle switch otherwise. Neither saved setting is touched.
    private func applyDeskLook() {
        let matching = theme.palette.ink != nil
        let font: String? = matching ? DeskLook.read(UserDefaults.desk).font : nil
        if FontSettings.shared.deskFamily != font { FontSettings.shared.deskFamily = font }
        if DisplaySettings.shared.themeDrawsSubtle != matching {
            DisplaySettings.shared.themeDrawsSubtle = matching
        }
    }

    /// Follows Desk's font, ink and shadow while Match Desk is current.
    @ObservationIgnored private var deskObserver: DeskLookObserver?
    @ObservationIgnored private var meterSettingsObserver: NSObjectProtocol?

    var compact: Bool = false {            // double-click title to toggle
        didSet {
            if oldValue != compact {
                NotificationCenter.default.post(
                    name: .sanduhrCompactDidChange, object: nil)
            }
        }
    }
    var pinned: Bool = true                // floats on top when true

    // MARK: Runtime state

    var usage: UsageResponse? {
        didSet { refreshMeterWarnings() }
    }
    /// The tiers whose card bar draws red with a glow: the same rule and settings as the Desk
    /// meters (MeterWarning, Settings, Desk, Meters). Re-applied when the numbers arrive, on the
    /// countdown tick (the reset draws nearer) and at once when a Meters setting changes.
    private(set) var warningTiers: Set<Tier> = []
    var lastUpdated: Date?
    var status: StatusMessage = .connecting
    var history: HistoryStore.History = HistoryStore.load()
    /// Bumped every 30s so countdown labels re-render without refetching.
    var countdownTick: Int = 0 {
        didSet { refreshMeterWarnings() }
    }
    /// Bumped whenever the user installs/reloads/deletes a theme so the
    /// theme dropdown re-reads `ThemeRegistry.themes`. SwiftUI can't
    /// observe a static registry otherwise. The current theme is re-read by id at each bump.
    var userThemesTick: Int = 0 {
        didSet { reresolveTheme() }
    }
    /// The overlay open on the widget (Deep Work or Cooldown Snake), nil for the cards. The
    /// Tools items in every menu set it and show it checked.
    var activeTool: WidgetTool?
    /// The pacing calculators (cool down, surplus) stay showing on every card instead of only
    /// under the pointer. Turned on and off from the Tools items and Settings, Pacing & Focus; not saved.
    var pacingPinned: Bool = false

    /// The widget overlays a menu can ask for.
    enum WidgetTool { case deepWork, snake }

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
        /// After Sign Out: no credentials, nothing fetched until a new key is saved.
        case signedOut

        var text: String {
            switch self {
            case .connecting:           return "Connecting…"
            case .refreshing:           return "Refreshing…"
            case .idle, .noTiers:       return ""
            case .error(let m, _):      return m
            case .signedOut:            return "Signed out — sign in"
            }
        }
        var isError: Bool {
            if case .error = self { return true }
            return false
        }
        /// Only a new sign-in helps: Desk and the notch show the sign-in line.
        var needsSignIn: Bool {
            switch self {
            case .signedOut:            return true
            case .error(_, let isAuth): return isAuth
            default:                    return false
            }
        }
    }

    // MARK: Private

    private var api: ClaudeAPI?
    private var refreshTimer: Timer?
    private var countdownTimer: Timer?

    /// 5 min between API calls, mirroring sanduhr.py:37.
    private let refreshInterval: TimeInterval = 5 * 60
    /// 30 s between countdown UI updates, mirroring sanduhr.py:38.
    private let countdownInterval: TimeInterval = 30

    // MARK: Init

    init() {
        let stored = UserDefaults.standard.string(forKey: "theme") ?? ""
        self.theme = Self.resolve(stored) ?? ThemeRegistry.default
        applyDeskLook()
        deskObserver = DeskLookObserver { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.theme.palette.ink != nil else { return }
                self.reresolveTheme()
            }
        }
        // A Meters setting changed in Settings: restyle the cards now. Any in-process defaults
        // change counts (cheap: an unchanged set is not reassigned). A `defaults write` from
        // another process posts nothing; the countdown tick picks that up.
        meterSettingsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.refreshMeterWarnings() }
        }
    }

    /// Called once the app has a window + a session key.
    /// Checks `exists()` first so a first launch goes straight to onboarding.
    func bootstrap() {
        guard KeychainStore.exists(account: KeychainAccount.sessionKey) else {
            status = .connecting     // onboarding sheet will drive the next step
            return
        }
        if let key = KeychainStore.get(account: KeychainAccount.sessionKey),
           !key.isEmpty {
            let cf = KeychainStore.get(account: KeychainAccount.cfClearance)
            api = ClaudeAPI(sessionKey: key, cfClearance: cf)
            Task { await refresh() }
            startTimers()
        } else {
            status = .connecting
        }
    }

    /// Called after the user saves new credentials: rebuilds the API client and refreshes.
    func credentialsChanged() {
        guard let key = KeychainStore.get(account: KeychainAccount.sessionKey),
              !key.isEmpty else { return }
        let cf = KeychainStore.get(account: KeychainAccount.cfClearance)
        api = ClaudeAPI(sessionKey: key, cfClearance: cf)
        Task { await refresh() }
        startTimers()
    }

    /// Settings, Credentials, Sign Out (after its confirmation). Deletes the credentials from
    /// both stores, stops fetching, drops the shown numbers and writes snapshot.json signed out;
    /// history and settings stay. Saving a key again goes through `credentialsChanged()`.
    @discardableResult
    func signOut() -> SignOutResult {
        let result = KeychainStore.signOut()
        refreshTimer?.invalidate()
        refreshTimer = nil
        countdownTimer?.invalidate()
        countdownTimer = nil
        api = nil
        usage = nil
        lastUpdated = nil
        status = .signedOut
        SnapshotWriter.writeSignedOut()
        onUsageUpdate?()
        return result
    }

    // MARK: Timers

    private func startTimers() {
        refreshTimer?.invalidate()
        countdownTimer?.invalidate()

        // Common modes, so an open menu or a drag does not hold up a refresh.
        let refresh = Timer(timeInterval: refreshInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.refresh() }
        }
        refresh.tolerance = 30
        RunLoop.main.add(refresh, forMode: .common)
        refreshTimer = refresh
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
        let u: UsageResponse
        do {
            u = try await api.getUsage()
        } catch {
            // Signed out (or a new key saved) while this fetch ran: its answer is stale.
            guard self.api === api else { return }
            showFailure(error)
            return
        }
        guard self.api === api else { return }
        self.usage = u
        self.lastUpdated = Date()
        Notifier.shared.evaluate(u)
        SnapshotWriter.writeOk(u)
        for (tier, t) in u.tiers {
            if let util = t.utilization {
                HistoryStore.append(tier, utilization: util)
            }
        }
        self.history = HistoryStore.load()
        self.status = u.tiers.isEmpty ? .noTiers : .idle
    }

    /// A failed fetch: the status line and snapshot.json's error kind.
    private func showFailure(_ error: Error) {
        switch error as? ClaudeAPI.APIError {
        case .unauthorized?:
            status = .error("Session expired — click Key", isAuth: true)
            SnapshotWriter.writeError("session_expired")
        case .cloudflareChallenge?:
            status = .error("Cloudflare — add cf_clearance", isAuth: true)
            SnapshotWriter.writeError("cloudflare")
        case .http(let c)?:
            status = .error("HTTP \(c)", isAuth: false)
            SnapshotWriter.writeError("network")
        default:
            status = .error(error.localizedDescription, isAuth: false)
            SnapshotWriter.writeError("network")
        }
    }

    // MARK: UI helpers

    /// Re-applies the saved warning settings to the current numbers. The set is only reassigned
    /// when a tier turns red or back, so the cards only redraw then.
    func refreshMeterWarnings(now: Date = Date()) {
        let fresh = Self.warningTiers(usage, now: now, desk: UserDefaults.desk)
        if fresh != warningTiers { warningTiers = fresh }
    }

    /// The widget's warning tiers for these numbers with the Meters settings saved in `desk`:
    /// `MeterWarning.tiers`, the rule the Desk rows use.
    nonisolated static func warningTiers(_ usage: UsageResponse?, now: Date, desk: DefaultsStore) -> Set<Tier> {
        MeterWarning.tiers(usage, now: now) { MeterWarningSettings.saved($0, in: desk) }
    }

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
