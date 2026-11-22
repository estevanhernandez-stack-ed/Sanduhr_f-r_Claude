import AppKit
import ServiceManagement

/// The lock screen line's root helper as the app sees it (#137): registered with
/// `SMAppService.daemon` when Settings, Lock Screen's switch turns on, removed when it turns off.
/// Registering asks for an admin password once; macOS may also ask for an OK in System Settings,
/// General, Login Items. The helper itself is `Helpers/LockLineHelper` and its rules
/// `LockLineHelperRules`.
@MainActor
enum LockLineDaemon {
    /// The helper this copy registers: the dev label when build.sh gave the bundle that plist (an
    /// ad-hoc signature), else the release one.
    static var service: SMAppService { .daemon(plistName: LockLineHelperRules.plistName(label)) }

    /// 2.33.0's helper, only ever unregistered.
    static var legacy: SMAppService { .daemon(plistName: LockLineHelperRules.plistName(LockLineHelperRules.legacyLabel)) }

    static var label: String {
        let folder = Bundle.main.bundleURL.appendingPathComponent("Contents/Library/LaunchDaemons")
        let names = (try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? []
        return label(bundled: Set(names))
    }

    /// The label for the plists a bundle carries: the dev one when present, else the release one.
    nonisolated static func label(bundled: Set<String>) -> String {
        bundled.contains(LockLineHelperRules.plistName(LockLineHelperRules.devLabel))
            ? LockLineHelperRules.devLabel : LockLineHelperRules.label
    }

    /// What Settings says about the helper.
    enum State: String, Equatable {
        case off
        case needsApproval = "needs_approval"
        case on
        /// Registered and allowed, but launchd has not run it lately: a registration from an older
        /// build that stopped spawning after an update ("spawn failed", "needs LWCR update", seen
        /// 2026-11-01 on a registration from build 45). Only the user can bring it back: see
        /// `restartSteps` (2.39.3).
        case stalled
        case missing
        /// The switch is on but macOS has no registration (2.47.1, #356): a Login Items toggle, or a
        /// registration macOS dropped. The lock screen keeps the helper's last line. Only the user
        /// brings it back, with **Turn it on again**; Sanduhr never registers on its own.
        case unregistered
    }

    nonisolated static func state(_ status: SMAppService.Status) -> State {
        switch status {
        case .enabled: .on
        case .requiresApproval: .needsApproval
        case .notFound: .missing
        default: .off
        }
    }

    /// A 2.33.0 helper still registered counts as on, so its switch reads true and turning it
    /// off removes it.
    nonisolated static func state(_ status: SMAppService.Status, legacy: SMAppService.Status) -> State {
        let current = state(status)
        return current == .off && legacy == .enabled ? .on : current
    }

    /// With the switch: on and not registered (nor a 2.33.0 helper still enabled) is `.unregistered`;
    /// off and not registered stays `.off`.
    nonisolated static func state(enabled: Bool, status: SMAppService.Status, legacy: SMAppService.Status) -> State {
        let current = state(status, legacy: legacy)
        return enabled && current == .off ? .unregistered : current
    }

    /// The whole judge: the switch, macOS's statuses, then the heartbeat for a helper that reads on.
    nonisolated static func state(enabled: Bool, status: SMAppService.Status, legacy: SMAppService.Status,
                                  heartbeat: Date?, watchingSince: Date, now: Date) -> State {
        let base = state(enabled: enabled, status: status, legacy: legacy)
        guard base == .on, stalled(heartbeat: heartbeat, watchingSince: watchingSince, now: now) else { return base }
        return .stalled
    }

    static var state: State {
        state(enabled: UserDefaults.desk.bool(forKey: LockLineKeys.on), status: service.status,
              legacy: legacy.status, heartbeat: heartbeat(), watchingSince: watchingSince, now: Date())
    }

    // MARK: The line stopped updating (2.47.1, #356)

    /// The notification's identifier; a click on it opens Settings, Lock Screen (Notifier.clickTarget).
    nonisolated static let warningID = "lockline-stopped"
    nonisolated static let warningTitle = "The lock screen line stopped updating."
    nonisolated static let warningBody = "Settings, Lock Screen says how to bring it back."
    /// At most one warning in this long.
    nonisolated static let warnEvery: TimeInterval = 24 * 60 * 60

    /// Whether to post the warning now: the switch on, the helper stalled or not registered, the
    /// judge window past since launch, a wake or a registration, not in demo mode, not in Alerts'
    /// quiet hours (the caller records nothing then, so the next check after them fires), and
    /// none in the last 24 hours. Checked at launch, on wake and on the 5-minute recompose.
    nonisolated static func shouldWarn(state: State, enabled: Bool, demo: Bool, quiet: Bool, lastWarned: Date?,
                                       now: Date, watchingSince: Date) -> Bool {
        guard enabled, !demo, !quiet, state == .stalled || state == .unregistered else { return false }
        guard now.timeIntervalSince(watchingSince) >= judgeAfter else { return false }
        guard let lastWarned else { return true }
        return now.timeIntervalSince(lastWarned) >= warnEvery
    }

    // MARK: Is launchd still running it?

    /// The helper runs once a minute; a heartbeat older than this means it does not.
    nonisolated static let heartbeatMaxAge: TimeInterval = 5 * 60
    /// How long the app watches (since launch, a wake or a registration) before it judges.
    nonisolated static let judgeAfter: TimeInterval = 3 * 60

    /// Since when the helper could have run while the app watched: launch, the last wake, the last
    /// registration. A sleeping Mac runs nothing, so a wake starts the count again.
    static var watchingSince = Date()

    /// Stalled: the app has watched long enough and the heartbeat is missing or old.
    nonisolated static func stalled(heartbeat: Date?, watchingSince: Date, now: Date) -> Bool {
        guard now.timeIntervalSince(watchingSince) >= judgeAfter else { return false }
        guard let heartbeat else { return true }
        return now.timeIntervalSince(heartbeat) > heartbeatMaxAge
    }

    /// The heartbeat's date (`LockLineHelperRules.heartbeatPath`), nil when there is none yet.
    static func heartbeat() -> Date? {
        (try? FileManager.default.attributesOfItem(atPath: LockLineHelperRules.heartbeatPath))?[.modificationDate] as? Date
    }

    /// How a stalled helper comes back. 2.39.2 unregistered and registered it on its own, and
    /// macOS then refused the registration ("Operation not permitted", no prompt) until Sanduhr
    /// was switched off and on under Login Items, Allow in the Background, with an administrator
    /// password (seen 2026-11-02). Only the user can give that OK, so Sanduhr says how instead.
    static let restartSteps = "To restart it, turn this switch off, then in Login Items switch Sanduhr off and on under Allow in the Background (macOS asks for your password), then turn this switch on."

    /// One line under the switch for each state.
    static func caption(_ state: State) -> String {
        switch state {
        case .on: "On. The line shows the next time the screen locks; it follows the numbers within a few minutes."
        case .stalled: "Stopped: macOS hasn't run the lock screen helper lately, so the lock screen shows an old line. " + restartSteps
        case .needsApproval: "macOS needs your OK: System Settings, General, Login Items, allow Sanduhr in the background."
        case .missing: "This copy of Sanduhr has no lock screen helper in it."
        case .off: "Off. Turning it on asks for an administrator password once, to put the line on the lock screen."
        case .unregistered: "The lock screen helper isn't registered, so the lock screen keeps an old line."
        }
    }

    /// The button under `.unregistered`'s caption: the switch's own install path.
    static let turnOnAgain = "Turn it on again"

    /// Registers the helper. Throws what macOS says when it refuses (an unsigned build, a denied
    /// password), for Settings to show.
    /// A 2.33.0 helper goes once this one is registered: both run the same executable on the
    /// same files, so the line stays up through the swap.
    static func install() async throws {
        defer { if service.status == .enabled { dropLegacy() } }
        guard service.status != .enabled else { return }
        try service.register()
        watchingSince = Date()
    }

    private static func dropLegacy() {
        guard legacy.status == .enabled || legacy.status == .requiresApproval else { return }
        Task { @MainActor in try? await legacy.unregister() }
    }

    /// Removes the helper once it has taken Sanduhr's line off the lock screen (or put back the
    /// message that was there before). The app has already written an empty `lock-line.txt`
    /// and the helper runs once a minute, so this waits for the key to change, up to `clearWait`.
    /// It never unregisters while the line is still up: a helper removed first would leave the
    /// line there with nothing to take it away (seen on the first real turn-off). If the wait
    /// runs out, the helper stays and the launch sweep tries again. True when removed.
    @discardableResult
    static func remove(lastLine: String) async -> Bool {
        // A stalled helper would never take the line off, so waiting is pointless: it goes now,
        // and the old line stays until a running helper clears it (stalledOff says so).
        if state == .stalled {
            try? await service.unregister()
            return true
        }
        let deadline = Date().addingTimeInterval(clearWait)
        while !lastLine.isEmpty, currentLoginText() == lastLine {
            guard Date() < deadline else { return false }
            try? await Task.sleep(for: .seconds(5))
        }
        try? await service.unregister()
        if legacy.status != .notRegistered, legacy.status != .notFound { try? await legacy.unregister() }
        return true
    }

    static let clearWait: TimeInterval = 10 * 60

    /// What Settings says when `register()` threw: nothing when the helper ended up on or is only
    /// waiting for the Login Items OK (macOS throws in both cases on 26). A refusal ("Operation not
    /// permitted", seen after an unregister) needs Sanduhr allowed again in Login Items, so it gets
    /// the steps; anything else, macOS's reason.
    static func installProblem(_ error: Error, after state: State) -> String? {
        switch state {
        case .on, .needsApproval: nil
        case .off, .missing, .stalled, .unregistered:
            refused(error)
                ? "macOS didn't let Sanduhr start the lock screen helper. In Login Items, switch Sanduhr off and on under Allow in the Background (macOS asks for your password), then turn this switch off and on."
                : "macOS did not install the lock screen helper: \(error.localizedDescription)"
        }
    }

    /// "Operation not permitted": EPERM, as macOS reports a registration it won't allow.
    nonisolated static func refused(_ error: Error) -> Bool {
        let e = error as NSError
        if e.domain == NSPOSIXErrorDomain, e.code == Int(EPERM) { return true }
        return e.localizedDescription.localizedCaseInsensitiveContains("operation not permitted")
    }

    /// After turning off a stalled helper: it is gone, but the last line it set stays.
    static let stalledOff = "The helper is off, but the lock screen keeps its old line until a running helper clears it. " + restartSteps + " Then turn it off again if you want the line gone."

    /// `LoginwindowText` as macOS has it now (world-readable, so no privilege needed).
    static func currentLoginText() -> String? {
        CFPreferencesCopyValue(LockLineHelperRules.key as CFString, LockLineHelperRules.domain as CFString,
                               kCFPreferencesAnyUser, kCFPreferencesAnyHost) as? String
    }

    static let removingCaption = "Taking the line off the lock screen; this can take a minute."
    static let stillUp = "The line is still on the lock screen. Sanduhr keeps the helper until it is gone and tries again at its next launch."

    static func openLoginItems() { SMAppService.openSystemSettingsLoginItems() }
}
