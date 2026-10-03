import Foundation
import Observation
import Sparkle

/// Sparkle's own update settings for Settings, Updates. The switches read and write the
/// `SPUUpdater` directly, so Sparkle keeps storing them in the app's defaults and the menus'
/// Check for Updates… and this page always agree. Changes made elsewhere (Sparkle's first-run
/// permission prompt) arrive by KVO.
@MainActor
@Observable
final class UpdaterSettings {
    /// False while a check runs (or before the updater has started): Check Now is disabled.
    private(set) var canCheck = false
    private(set) var lastCheck: Date?
    private(set) var checksAutomatically = false
    private(set) var downloadsAutomatically = false
    /// Sparkle can install by itself (the app is not sandboxed away from it, and the Info.plist
    /// does not forbid it).
    private(set) var allowsAutomaticDownloads = false

    @ObservationIgnored private let controller: SPUStandardUpdaterController
    @ObservationIgnored private var observations: [NSKeyValueObservation] = []

    init(controller: SPUStandardUpdaterController) {
        self.controller = controller
        let updater = controller.updater
        // Sparkle posts these on the main thread; hop there anyway so nothing reads it elsewhere.
        let changed: @Sendable () -> Void = { [weak self] in
            Task { @MainActor in self?.sync() }
        }
        observations = [
            updater.observe(\.canCheckForUpdates) { _, _ in changed() },
            updater.observe(\.automaticallyChecksForUpdates) { _, _ in changed() },
            updater.observe(\.automaticallyDownloadsUpdates) { _, _ in changed() },
            updater.observe(\.allowsAutomaticUpdates) { _, _ in changed() },
        ]
        sync()
    }

    /// Reads the updater again. A finished check flips `canCheckForUpdates` back, which also
    /// brings in the new last-check date (that property posts no change notice of its own).
    func sync() {
        let updater = controller.updater
        canCheck = updater.canCheckForUpdates
        lastCheck = updater.lastUpdateCheckDate
        checksAutomatically = updater.automaticallyChecksForUpdates
        downloadsAutomatically = updater.automaticallyDownloadsUpdates
        allowsAutomaticDownloads = updater.allowsAutomaticUpdates
    }

    /// Check Now: the same user-initiated check as the menus' Check for Updates….
    func checkNow() {
        controller.checkForUpdates(nil)
        sync()
    }

    func setChecksAutomatically(_ on: Bool) {
        controller.updater.automaticallyChecksForUpdates = on
        sync()
    }

    func setDownloadsAutomatically(_ on: Bool) {
        controller.updater.automaticallyDownloadsUpdates = on
        sync()
    }
}
