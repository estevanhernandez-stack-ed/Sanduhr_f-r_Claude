import Foundation
import SwiftUI
import Combine
import os

/// Hidden limits showing again (item 42): tier raw values and reasons only, never labels.
private let limitsLog = Logger(subsystem: "com.626labs.sanduhr", category: "limits")

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
    /// changed under Match Desk, restyles the widget without being picked again. A user theme
    /// whose file is gone falls back to the default at once (and says so in the log); one whose
    /// file is still there but no longer loads (half-saved in an editor) stays as it is.
    func reresolveTheme() {
        switch Self.afterReload(theme, fileExists: { UserThemes.fileIDs().contains($0) }) {
        case .keep: break
        case .update(let fresh): theme = fresh
        case .fallBack(let fallback):
            NSLog("[Sanduhr] Theme \(theme.id) was removed from the themes folder; using \(fallback.displayName)")
            theme = fallback
        }
        applyDeskLook()
    }

    /// What a reload means for the current theme.
    enum ThemeAfterReload: Equatable {
        /// Unchanged, or no longer loading while its file is still there.
        case keep
        /// The registry (or Desk, for Match Desk) has a newer copy.
        case update(Theme)
        /// Its file is gone: the default theme.
        case fallBack(Theme)
    }

    /// The current theme after the themes folder was read again. `fileExists` says whether the
    /// folder has a file for an id; it is asked only when the theme no longer resolves.
    nonisolated static func afterReload(_ current: Theme, fileExists: (String) -> Bool,
                                        desk: DefaultsStore = UserDefaults.desk) -> ThemeAfterReload {
        if ThemeRegistry.theme(id: current.id) != nil {
            return reresolved(current, desk: desk).map { .update($0) } ?? .keep
        }
        if fileExists(current.id) { return .keep }
        return .fallBack(resolve(ThemeRegistry.default.id, desk: desk) ?? ThemeRegistry.default)
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
    /// Reloads the user themes when a file in the themes folder changes outside the app.
    @ObservationIgnored private var themeWatcher: ThemeFolderWatcher?

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
    /// The limits switched off in Settings, Desk, Meters (MeterVisibility): left off the cards
    /// and out of `warningTiers`. Re-read with the warnings; a change resizes the panel.
    private(set) var hiddenTiers: Set<Tier> = MeterVisibility.hidden(in: UserDefaults.desk)
    /// The limits believed temporary with the current numbers (LimitLifetime): the only ones a
    /// card's menu offers to hide. Re-read with the warnings.
    private(set) var temporaryTiers: Set<Tier> = []
    var lastUpdated: Date?
    var status: StatusMessage = .connecting
    /// An account switch's crossfade (AccountSwitchFade): the old account's cards and Desk meters
    /// have faded out and stay in the layout unseen, so nothing jumps, until the new numbers land.
    private(set) var switchVeil = false
    /// The faint "Switching account…" while a switch's fetch outlasts the fade.
    private(set) var switchNote = false
    /// The departing account's name, kept on the chip and the Desk line until the old numbers
    /// have faded out (AccountSwitchFade.NameHold).
    private(set) var nameHold = AccountSwitchFade.NameHold()
    /// The old account's numbers and history, held only as the veiled layout; nil outside a switch.
    @ObservationIgnored private var departingUsage: UsageResponse?
    @ObservationIgnored private var departingHistory: HistoryStore.History?
    @ObservationIgnored private var switchStartedAt: Date?
    @ObservationIgnored private var switchNoteTask: Task<Void, Never>?

    /// What the cards and the Desk meters lay out: the numbers, or during a switch the old
    /// account's, drawn unseen (`switchVeil`). Everything else (the menu bar, alerts,
    /// snapshot.json) reads `usage`, which a switch clears at once.
    var shownUsage: UsageResponse? { usage ?? (switchVeil ? departingUsage : nil) }
    /// The sparklines that go with `shownUsage`.
    var shownHistory: HistoryStore.History {
        switchVeil && usage == nil ? departingHistory ?? history : history
    }
    /// The active account's sparklines: the recent window of its history (HistoryStore.sparklines),
    /// never the 30 days on file.
    var history: HistoryStore.History = HistoryStore.sparklines(HistoryStore.load(account: KeychainStore.accounts.active))
    /// The accounts whose Meter history is off (MeterHistory), as Settings, Accounts shows them.
    private(set) var historyOffAccounts: Set<String> = Set(MeterHistory.offLabels(in: KeychainStore.accounts.defaults))
    /// The accounts in list order and the active one, as the Accounts page, the menus and the
    /// widget chip show them. Read from the registry's defaults (never the Keychain) and kept
    /// current by every account change here (`reloadAccounts`).
    private(set) var accountLabels: [String] = KeychainStore.accounts.labels
    private(set) var activeAccount: String? = KeychainStore.accounts.active
    /// The accounts with a session key in this launch's store (asked without reading a value).
    private(set) var signedInAccounts: Set<String> = UsageViewModel.signedIn(KeychainStore.accounts)

    /// Settings, Accounts, Follow the account I'm using: off by default (AccountFollower).
    var followEnabled: Bool = UserDefaults.standard.bool(forKey: AccountFollower.enabledKey) {
        didSet {
            guard followEnabled != oldValue else { return }
            UserDefaults.standard.set(followEnabled, forKey: AccountFollower.enabledKey)
            follower.apply(enabled: followEnabled)
            refreshInUse()
        }
    }
    /// The other accounts following saw in use: "· in use" in the Accounts menu, a dot on the chip.
    private(set) var accountsInUse: Set<String> = []
    /// For a minute after an automatic switch the chip and the Desk line read "Label (in use)".
    private(set) var followNote = false
    @ObservationIgnored private let follower = AccountFollower()
    @ObservationIgnored private var followNoteTask: Task<Void, Never>?
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

    enum StatusMessage: Equatable {
        case connecting
        case refreshing
        case idle
        case noTiers
        case error(String, isAuth: Bool)
        /// After Sign Out: no credentials, nothing fetched until a new key is saved.
        case signedOut
        /// An account switch, until the new account's first fetch answers.
        case switching

        var text: String {
            switch self {
            case .connecting:           return "Connecting…"
            case .refreshing:           return "Refreshing…"
            case .idle, .noTiers:       return ""
            case .error(let m, _):      return m
            case .signedOut:            return "Signed out — sign in"
            case .switching:            return "Switching account…"
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
    /// The account `api` was built for (nil while a launch runs on the legacy key): history and
    /// snapshot.json's `account_ref` follow the client, not whatever is active when a fetch ends.
    private var apiAccount: String?
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
        // A theme file added, edited or deleted in Finder or an editor: reload, and the tick
        // re-lists the gallery and the Theme menu and re-reads (or drops) the current theme.
        themeWatcher = ThemeFolderWatcher { [weak self] in
            MainActor.assumeIsolated {
                UserThemes.reload()
                self?.userThemesTick &+= 1
            }
        }
        follower.context = { [weak self] in self?.followContext() ?? AccountFollow.State() }
        follower.onSwitch = { [weak self] label in self?.switchAccount(to: label, automatic: true) }
        follower.onReadings = { [weak self] in self?.refreshInUse() }
        follower.apply(enabled: followEnabled)
    }

    /// Called once the app has a window + a session key.
    /// Checks `exists()` first so a first launch goes straight to onboarding.
    func bootstrap() {
        guard KeychainStore.exists(account: KeychainAccount.sessionKey) else {
            status = .connecting     // onboarding sheet will drive the next step
            return
        }
        if connect() {
            Task { await refresh() }
            startTimers()
        } else {
            status = .connecting
        }
    }

    /// Called after the user saves new credentials: rebuilds the API client and refreshes.
    func credentialsChanged() {
        reloadAccounts()
        guard connect() else { return }
        // The first key saved creates Personal, whose history may be the upgrade's.
        history = HistoryStore.sparklines(HistoryStore.load(account: apiAccount))
        Task { await refresh() }
        startTimers()
    }

    /// A fresh client from the active account's key: no cookie or org id carries over from
    /// another account (the org rule from item 35 runs again). False when it has no key.
    @discardableResult
    private func connect() -> Bool {
        guard let key = KeychainStore.get(account: KeychainAccount.sessionKey), !key.isEmpty else { return false }
        let cf = KeychainStore.get(account: KeychainAccount.cfClearance)
        api = ClaudeAPI(sessionKey: key, cfClearance: cf)
        apiAccount = KeychainStore.accounts.active
        return true
    }

    /// Settings, Credentials, Sign Out (after its confirmation): signs out the active account.
    /// Deletes its credentials from both stores, stops fetching, drops the shown numbers and
    /// writes snapshot.json signed out; the account stays listed, and history and settings stay.
    /// Saving a key again goes through `credentialsChanged()`.
    @discardableResult
    func signOut() -> SignOutResult {
        let active = KeychainStore.accounts.active
        let result = KeychainStore.signOut(label: active)
        stopFetching()
        status = .signedOut
        SnapshotWriter.writeSignedOut(accountRef: AccountRef.of(active))
        reloadAccounts()
        onUsageUpdate?()
        return result
    }

    /// Sign Out for any account: the active one as `signOut()`, another one quietly.
    @discardableResult
    func signOut(account label: String) -> SignOutResult {
        guard label != KeychainStore.accounts.active else { return signOut() }
        let result = KeychainStore.signOut(label: label)
        follower.forget(label)
        reloadAccounts()
        refreshInUse()
        return result
    }

    /// Remove Account (after its own confirmation): Sign Out, its history file deleted, dropped
    /// from the list. Removing the active account shows the next one, or the no-account state
    /// when it was the last.
    @discardableResult
    func removeAccount(_ label: String) -> SignOutResult {
        let wasActive = label == KeychainStore.accounts.active
        let result = KeychainStore.remove(label: label)
        follower.forget(label)
        if wasActive { showActiveAccount(leaving: accountLabel) } else { reloadAccounts() }
        refreshInUse()
        return result
    }

    /// Rename an account; its secrets, history file and sign-in marker follow (AccountRegistry).
    func renameAccount(_ old: String, to new: String) throws {
        try KeychainStore.accounts.rename(old, to: new)
        if apiAccount == old { apiAccount = new }
        follower.rename(old, to: new)
        reloadAccounts()
        refreshInUse()
        onUsageUpdate?()
    }

    /// Settings, Accounts, Meter history (item 43): Off stops recording the account's meters from
    /// its next fetch; what was kept stays until `eraseHistory`. 30 days records again.
    func setMeterHistory(_ label: String, on: Bool) {
        MeterHistory.set(on, for: label, in: KeychainStore.accounts.defaults)
        reloadAccounts()
        onUsageUpdate?()
    }

    /// Deletes the account's history file (after the confirmation Off offers). The active
    /// account's sparklines empty at once.
    func eraseHistory(_ label: String) {
        HistoryStore.Files.standard.delete(label)
        if label == KeychainStore.accounts.active { history = [:] }
        onUsageUpdate?()
    }

    /// Settings, Accounts, Add Account: a new account with its key, at the end of the list. It
    /// is fetched at once when it becomes active: asked for, or the first account there is.
    /// Refusals (`AccountError`) name no label and no value, so their text can be shown as is.
    func addAccount(_ label: String, sessionKey: String, cfClearance: String?, makeActive: Bool) throws {
        let accounts = KeychainStore.accounts
        let wasEmpty = accounts.labels.isEmpty
        try accounts.add(label, sessionKey: sessionKey, cfClearance: cfClearance)
        if wasEmpty {
            // The first account: no legacy copy may outlive it (KeychainStore.set does the same).
            accounts.dropLegacy()
            accounts.adoptLegacyHistory()
            credentialsChanged()
        } else if makeActive {
            switchAccount(to: label)
        } else {
            reloadAccounts()
        }
    }

    /// Settings, Accounts, Save for one account: a value replaces, nil keeps, "" deletes. Saving
    /// the active account's key fetches with it at once.
    func saveCredentials(_ label: String, sessionKey: String?, cfClearance: String?) throws {
        try KeychainStore.accounts.save(label, sessionKey: sessionKey, cfClearance: cfClearance)
        if label == KeychainStore.accounts.active { credentialsChanged() } else { reloadAccounts() }
    }

    /// Re-reads the account list and the active label from the registry. Assigns only on a
    /// change, so views redraw only then.
    func reloadAccounts() {
        let accounts = KeychainStore.accounts
        let labels = accounts.labels
        let active = accounts.active
        if labels != accountLabels { accountLabels = labels }
        if active != activeAccount { activeAccount = active }
        let signedIn = Self.signedIn(accounts)
        if signedIn != signedInAccounts { signedInAccounts = signedIn }
        let off = Set(MeterHistory.offLabels(in: accounts.defaults))
        if off != historyOffAccounts { historyOffAccounts = off }
    }

    nonisolated private static func signedIn(_ accounts: AccountRegistry) -> Set<String> {
        Set(accounts.labels.filter(accounts.hasKey))
    }

    /// Two or more accounts: the chip, the Accounts submenu and the Desk's label show.
    var showsAccounts: Bool { accountLabels.count > 1 }

    /// What the widget's account chip shows.
    struct AccountChipText: Equatable {
        var text: String
        /// Another account is in use too (following saw both): a small dot beside the label.
        var otherInUse = false
    }

    /// The chip in the title area, nil with fewer than two accounts.
    var accountChip: AccountChipText? {
        guard let label = shownAccountLabel else { return nil }
        return AccountChipText(text: label, otherInUse: !accountsInUse.isEmpty)
    }

    /// The active label as the chip and the Desk line show it, nil with fewer than two
    /// accounts: "Work", or "Work (in use)" for a minute after an automatic switch.
    var accountLabel: String? {
        guard showsAccounts, let active = activeAccount else { return nil }
        return followNote ? "\(active) (in use)" : active
    }

    /// The name the chip and the Desk line show: `accountLabel`, except during a switch's fade
    /// out, when the departing account's name stays until its numbers are gone.
    var shownAccountLabel: String? { nameHold.shown(active: accountLabel) }

    // MARK: Following

    /// The view model's part of the decision state.
    /// Read from the registry each time (defaults, and Keychain attributes only), so a key
    /// saved or signed out anywhere counts at once.
    private func followContext() -> AccountFollow.State {
        let accounts = KeychainStore.accounts
        var s = AccountFollow.State()
        s.enabled = followEnabled
        s.active = accounts.active
        s.signedIn = Self.signedIn(accounts)
        s.activeSignedOut = status == .signedOut || api == nil
        s.switching = status == .switching
        return s
    }

    private func refreshInUse() {
        let fresh = AccountFollow.othersInUse(follower.current(), now: Date())
        if fresh != accountsInUse {
            accountsInUse = fresh
            onUsageUpdate?()
        }
    }

    /// Whether a manual switch is holding following back (state.yaml's `follow_paused`).
    var followPaused: Bool { AccountFollow.isPaused(follower.current(), now: Date()) }

    /// "Label (in use)" for a minute, then the plain label. No notification.
    private func showFollowNote(_ on: Bool) {
        followNoteTask?.cancel()
        followNoteTask = nil
        followNote = on
        guard on else { return }
        followNoteTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(AccountFollow.noteLength * 1_000_000_000))
            guard !Task.isCancelled, let self else { return }
            self.followNote = false
            self.onUsageUpdate?()
        }
    }

    // MARK: Accounts

    /// Switches the active account (spec "Switching"): sets it, deletes snapshot.json at once,
    /// clears the shown numbers and the menu bar percent, says "Switching account…", resets the
    /// alert baseline, loads that account's history, then builds a new client and fetches. A
    /// fetch still running for the old account is discarded when it answers (`refresh`).
    /// A switch from the chip, a menu or Settings pauses following (AccountFollow.isPaused).
    func switchAccount(to label: String) {
        switchAccount(to: label, automatic: false)
    }

    /// Both kinds of switch take the same path; an automatic one starts the 30-minute spacing
    /// and the minute of "(in use)", a manual one pauses following.
    private func switchAccount(to label: String, automatic: Bool) {
        let accounts = KeychainStore.accounts
        // The name as it showed before anything changes (the follow note included).
        let leaving = accountLabel
        guard label != accounts.active, (try? accounts.setActive(label)) != nil else { return }
        if automatic { follower.automaticSwitch() } else { follower.manualSwitch(to: label) }
        showFollowNote(automatic)
        showActiveAccount(leaving: leaving)
        refreshInUse()
    }

    /// The next account, wrapping (the widget chip's click, Windows `CycleAccount`). Nothing
    /// with fewer than two accounts.
    func cycleAccount() {
        guard let next = KeychainStore.accounts.nextLabel else { return }
        switchAccount(to: next)
    }

    /// After the active account changed: nothing of the old account stays on screen or in
    /// snapshot.json, then the new one is fetched, or shown signed out when it has no key.
    /// The old account's meters fade out (AccountSwitchFade) while the numbers themselves go at once;
    /// `leaving`, the name the chip showed, stays up until they are gone.
    private func showActiveAccount(leaving: String?) {
        let reduceMotion = AccountSwitchFade.reduceMotion
        withAnimation(AccountSwitchFade.outAnimation(reduceMotion: reduceMotion)) {
            beginSwitchVeil(leaving: leaving, reduceMotion: reduceMotion)
            SnapshotWriter.delete()
            stopFetching()
            Notifier.shared.resetForSwitch()
            reloadAccounts()
            let active = KeychainStore.accounts.active
            history = active.map { HistoryStore.sparklines(HistoryStore.load(account: $0)) } ?? [:]
            if connect() {
                status = .switching
                onUsageUpdate?()
                Task { await refresh() }
                startTimers()
            } else {
                status = .signedOut
                if active != nil { SnapshotWriter.writeSignedOut(accountRef: AccountRef.of(active)) }
                onUsageUpdate?()
            }
        }
    }

    /// Starts a switch's crossfade: the shown numbers are kept as the veiled layout and the
    /// departing name stays up. A second switch during the first keeps the first one's layout,
    /// name and clock.
    private func beginSwitchVeil(leaving: String?, reduceMotion: Bool) {
        if !switchVeil {
            departingUsage = usage
            departingHistory = history
            switchStartedAt = Date()
            switchVeil = true
            nameHold.begin(leaving: leaving, reduceMotion: reduceMotion)
        }
        switchNote = false
        switchNoteTask?.cancel()
        switchNoteTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(AccountSwitchFade.noteDelay * 1_000_000_000))
            guard let self, !Task.isCancelled, self.switchVeil else { return }
            // The old numbers are gone: the name crosses over to the new account.
            self.releaseName()
            if self.status == .switching {
                // The fetch outlasts the fade: the faint note.
                withAnimation(AccountSwitchFade.inAnimation(reduceMotion: AccountSwitchFade.reduceMotion)) {
                    self.switchNote = true
                    self.onUsageUpdate?()
                }
            } else {
                // Signed out, or the fetch already failed: nothing new is coming.
                self.endSwitchVeil()
            }
        }
    }

    /// The departing name gives way to the active one, crossfading on the chip.
    private func releaseName() {
        guard nameHold.holding else { return }
        withAnimation(AccountSwitchFade.nameAnimation(reduceMotion: AccountSwitchFade.reduceMotion)) {
            nameHold.release()
            onUsageUpdate?()
        }
    }

    /// Ends the crossfade: the new numbers (or the sign-in or error line) fade in.
    private func endSwitchVeil() {
        guard switchVeil else { return }
        switchNoteTask?.cancel()
        switchNoteTask = nil
        // The answer waited out the fade out (waitOutSwitchFade), so the old numbers are gone.
        releaseName()
        withAnimation(AccountSwitchFade.inAnimation(reduceMotion: AccountSwitchFade.reduceMotion)) {
            switchVeil = false
            switchNote = false
            departingUsage = nil
            departingHistory = nil
            switchStartedAt = nil
            onUsageUpdate?()
        }
    }

    /// A switch's first answer waits out the rest of the fade out, so the new numbers never land
    /// while the old ones are still fading.
    private func waitOutSwitchFade() async {
        guard switchVeil else { return }
        let wait = AccountSwitchFade.holdBack(since: switchStartedAt, now: Date(),
                                              reduceMotion: AccountSwitchFade.reduceMotion)
        guard wait > 0 else { return }
        try? await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000))
    }

    /// Stops the timers, drops the client (an answer still on its way is ignored) and the shown
    /// numbers.
    private func stopFetching() {
        refreshTimer?.invalidate()
        refreshTimer = nil
        countdownTimer?.invalidate()
        countdownTimer = nil
        api = nil
        apiAccount = nil
        usage = nil
        lastUpdated = nil
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
        let account = apiAccount
        let ref = AccountRef.of(account)
        // "Switching account…" stays up until the new account's first answer.
        if status != .switching { status = .refreshing }
        onUsageUpdate?()
        defer { onUsageUpdate?() }
        let u: UsageResponse
        do {
            u = try await api.getUsage()
        } catch {
            await waitOutSwitchFade()
            // Signed out, switched, or a new key saved while this fetch ran: its answer is stale.
            guard self.api === api else { return }
            showFailure(error, accountRef: ref)
            endSwitchVeil()
            return
        }
        await waitOutSwitchFade()
        guard self.api === api else { return }
        // Hidden limits that reset, refilled or are no longer temporary show again before the
        // numbers do (item 42).
        Self.reconcileHidden(u, now: Date())
        self.usage = u
        self.lastUpdated = Date()
        Notifier.shared.evaluate(u)
        SnapshotWriter.writeOk(u, accountRef: ref)
        follower.recordActive(account, usage: u)
        // One write of the history file per fetch, off the main thread, skipped while the
        // account's Meter history is off (item 43). The sparklines gain the same points in memory.
        let readings: [(Tier, Double)] = u.tiers.compactMap { tier, t in t.utilization.map { (tier, $0) } }
        let now = Date()
        if HistoryStore.record(readings, account: account, defaults: KeychainStore.accounts.defaults, now: now) {
            self.history = HistoryStore.sparklines(history, adding: readings, at: now)
        }
        self.status = u.tiers.isEmpty ? .noTiers : .idle
        // A switch's new numbers: they fade in.
        endSwitchVeil()
    }

    /// A failed fetch: the status line and snapshot.json's error kind.
    private func showFailure(_ error: Error, accountRef ref: String?) {
        switch error as? ClaudeAPI.APIError {
        case .unauthorized?:
            status = .error("Session expired — click Key", isAuth: true)
            SnapshotWriter.writeError("session_expired", accountRef: ref)
        case .cloudflareChallenge?:
            status = .error("Cloudflare — add cf_clearance", isAuth: true)
            SnapshotWriter.writeError("cloudflare", accountRef: ref)
        case .http(let c)?:
            status = .error("HTTP \(c)", isAuth: false)
            SnapshotWriter.writeError("network", accountRef: ref)
        default:
            status = .error(error.localizedDescription, isAuth: false)
            SnapshotWriter.writeError("network", accountRef: ref)
        }
    }

    // MARK: UI helpers

    /// Re-applies the saved warning settings to the current numbers. The set is only reassigned
    /// when a tier turns red or back, so the cards only redraw then.
    func refreshMeterWarnings(now: Date = Date()) {
        let hidden = MeterVisibility.hidden(in: UserDefaults.desk)
        if hidden != hiddenTiers {
            hiddenTiers = hidden
            // Cards came or went: the panel fits its new height, as after a compact toggle.
            NotificationCenter.default.post(name: .sanduhrCompactDidChange, object: nil)
        }
        let fresh = Self.warningTiers(usage, now: now, desk: UserDefaults.desk)
        if fresh != warningTiers { warningTiers = fresh }
        let temporary = MeterVisibility.temporary(usage, now: now, store: UserDefaults.desk)
        if temporary != temporaryTiers { temporaryTiers = temporary }
    }

    /// MeterVisibility.reconcile over fresh numbers, with each limit that shows again logged by
    /// its raw value and the reason, never its label.
    private static func reconcileHidden(_ usage: UsageResponse, now: Date) {
        let shown = MeterVisibility.reconcile(usage, now: now, store: UserDefaults.desk)
        for tier in Tier.allCases {
            guard let why = shown[tier] else { continue }
            limitsLog.info("hidden limit \(tier.rawValue, privacy: .public) shows again: \(why.rawValue, privacy: .public)")
        }
    }

    /// The widget's warning tiers for these numbers with the Meters settings saved in `desk`:
    /// `MeterWarning.tiers`, the rule the Desk rows use, over the limits that show.
    nonisolated static func warningTiers(_ usage: UsageResponse?, now: Date, desk: DefaultsStore) -> Set<Tier> {
        let shown = MeterVisibility.visible(usage, hidden: MeterVisibility.hidden(in: desk))
        return MeterWarning.tiers(shown, now: now) { MeterWarningSettings.saved($0, in: desk) }
    }

    /// Returns the tiers the server reported (with a non-nil utilization) that are not hidden
    /// (MeterVisibility), in display order. If compact mode is on, returns only the highest of
    /// those, so a hidden limit never takes compact mode's one card. Read from `shownUsage`, so
    /// during a switch these are the old account's veiled cards.
    /// Mirrors sanduhr.py:468-478.
    func visibleTiers() -> [(tier: Tier, usage: TierUsage)] {
        guard let u = MeterVisibility.visible(shownUsage, hidden: hiddenTiers) else { return [] }
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
