import AppKit
import ServiceManagement

/// The lock screen line's root helper as the app sees it (#137): registered with
/// `SMAppService.daemon` when Settings, Lock Screen's switch turns on, removed when it turns off.
/// Registering asks for an admin password once; macOS may also ask for an OK in System Settings,
/// General, Login Items. The helper itself is `Helpers/LockLineHelper` and its rules
/// `LockLineHelperRules`.
@MainActor
enum LockLineDaemon {
    static var service: SMAppService { .daemon(plistName: LockLineHelperRules.plistName) }

    /// What Settings says about the helper.
    enum State: Equatable {
        case off
        case needsApproval
        case on
        case missing
    }

    static func state(_ status: SMAppService.Status) -> State {
        switch status {
        case .enabled: .on
        case .requiresApproval: .needsApproval
        case .notFound: .missing
        default: .off
        }
    }

    static var state: State { state(service.status) }

    /// One line under the switch for each state.
    static func caption(_ state: State) -> String {
        switch state {
        case .on: "On. The line shows the next time the screen locks; it follows the numbers within a few minutes."
        case .needsApproval: "macOS needs your OK: System Settings, General, Login Items, allow Sanduhr in the background."
        case .missing: "This copy of Sanduhr has no lock screen helper in it."
        case .off: "Off. Turning it on asks for an administrator password once, to put the line on the lock screen."
        }
    }

    /// Registers the helper. Throws what macOS says when it refuses (an unsigned build, a denied
    /// password), for Settings to show.
    static func install() throws {
        guard service.status != .enabled else { return }
        try service.register()
    }

    /// Removes the helper after it has taken Sanduhr's line off the lock screen (or put back the
    /// message that was there before): the app has already written an empty `lock-line.txt`,
    /// and the helper runs once a minute, so this waits up to `clearWait` for the key to change.
    static func remove(lastLine: String) async {
        let deadline = Date().addingTimeInterval(clearWait)
        while Date() < deadline, currentLoginText() == lastLine, !lastLine.isEmpty {
            try? await Task.sleep(for: .seconds(5))
        }
        try? await service.unregister()
    }

    static let clearWait: TimeInterval = 90

    /// What Settings says when `register()` threw: nothing when the helper ended up on or is only
    /// waiting for the Login Items OK (macOS throws in both cases on 26), else macOS's reason.
    static func installProblem(_ error: Error, after state: State) -> String? {
        switch state {
        case .on, .needsApproval: nil
        case .off, .missing: "macOS did not install the lock screen helper: \(error.localizedDescription)"
        }
    }

    /// `LoginwindowText` as macOS has it now (world-readable, so no privilege needed).
    static func currentLoginText() -> String? {
        CFPreferencesCopyValue(LockLineHelperRules.key as CFString, LockLineHelperRules.domain as CFString,
                               kCFPreferencesAnyUser, kCFPreferencesAnyHost) as? String
    }

    static func openLoginItems() { SMAppService.openSystemSettingsLoginItems() }
}
