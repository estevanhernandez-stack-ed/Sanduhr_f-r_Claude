import Foundation
import Observation
import UserNotifications

/// Where macOS's notification permission for Sanduhr stands, as Settings ▸ Notifications needs it.
enum NotificationPermission: Equatable {
    /// Not read yet.
    case checking
    /// macOS has not been asked.
    case notDetermined
    case denied
    case allowed

    init(_ status: UNAuthorizationStatus) {
        switch status {
        case .notDetermined: self = .notDetermined
        case .denied: self = .denied
        default: self = .allowed
        }
    }

    /// Review Focus 5: with the permission off, the banner is dropped by macOS and nothing retries. The page says
    /// so, and says the rest still works.
    static let deniedLine = "macOS has notifications off for Sanduhr, so no notification will show. The glow and the Desk card still work. Turn them on in System Settings."
    static let notAskedLine = "Sanduhr hasn't asked macOS to show notifications yet."
    static let openSettings = "Open System Settings"
    static let allow = "Allow Notifications"
    static let settingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.notifications")!
}

/// The permission, read and asked for on the page's terms: never at launch, and asked only when a surface that needs
/// it is switched on (or tried) while macOS has not been asked. Reading and asking are injected, so tests never
/// touch Notification Center.
@MainActor
@Observable
final class NotificationPermissionModel {
    private(set) var state: NotificationPermission = .checking

    @ObservationIgnored private let read: (@escaping (NotificationPermission) -> Void) -> Void
    @ObservationIgnored private let request: (@escaping (Bool) -> Void) -> Void

    /// Reads nothing and asks nothing until a caller does.
    init(read: @escaping (@escaping (NotificationPermission) -> Void) -> Void = NotificationPermissionModel.liveRead,
         request: @escaping (@escaping (Bool) -> Void) -> Void = NotificationPermissionModel.liveRequest) {
        self.read = read
        self.request = request
    }

    /// The system's answer now.
    func refresh() {
        read { [weak self] s in
            // The closure may arrive off the main thread; the real read hops first.
            MainActor.assumeIsolated { self?.state = s }
        }
    }

    /// Asks macOS when it has not been asked; a granted or denied Mac is left alone (a denied one is never asked again,
    /// and could not answer).
    func requestIfNeeded() {
        guard state == .notDetermined else { return }
        request { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
    }

    /// Runs `then` once the permission question is settled: asks first when macOS has not been asked, then runs it
    /// whatever the answer (a Try it still shows the glow and the card).
    func ensure(_ then: @escaping () -> Void) {
        guard state == .notDetermined else { then(); return }
        request { [weak self] _ in
            MainActor.assumeIsolated {
                self?.refresh()
                then()
            }
        }
    }

    var showsDeniedLine: Bool { state == .denied }
    var showsNotAskedLine: Bool { state == .notDetermined }
    func deniedLineShows(bannerOn: Bool) -> Bool { bannerOn && state == .denied }
    func notAskedLineShows(bannerOn: Bool) -> Bool { bannerOn && state == .notDetermined }

    nonisolated static func liveRead(_ done: @escaping (NotificationPermission) -> Void) {
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            let state = NotificationPermission(settings.authorizationStatus)
            DispatchQueue.main.async { done(state) }
        }
    }

    nonisolated static func liveRequest(_ done: @escaping (Bool) -> Void) {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, _ in
            DispatchQueue.main.async { done(granted) }
        }
    }
}
