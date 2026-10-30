import Foundation
import Observation

/// CI watchers' switch and wiring (spec section 15): "Show my CI runners' jobs", off by default.
/// The service runs only while the switch is on and the Desk shows; this owns the pair, follows
/// the setting, and keeps the status line under the switch.
@MainActor
@Observable
final class RunnerWatchController {
    static let shared = RunnerWatchController.standard()

    /// Settings, Watchers: "Show my CI runners' jobs", off by default (Windows: `watchers_ci_runners`).
    nonisolated static let switchKey = "watchersCiRunners"

    private(set) var status = RunnerStatus.off

    @ObservationIgnored private let service: RunnerWatchService
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private var observer: NSObjectProtocol?

    init(defaults: UserDefaults = .standard, deskRunning: @escaping () -> Bool, docker: DockerAPI,
         sink: RunnerSink, now: @escaping () -> Date = Date.init, replayWait: Duration = .seconds(2)) {
        self.defaults = defaults
        service = RunnerWatchService(
            docker: docker, enabled: { defaults.bool(forKey: RunnerWatchController.switchKey) && deskRunning() },
            sink: sink, now: now, replayWait: replayWait)
        service.onStatusChange = { [weak self] in
            guard let self else { return }
            self.status = self.service.status
        }
    }

    static func standard() -> RunnerWatchController {
        RunnerWatchController(deskRunning: { DeskController.shared.running }, docker: DockerSocket(),
                              sink: WatcherStore.shared)
    }

    var isOn: Bool { defaults.bool(forKey: Self.switchKey) }

    /// What the line under the switch says: the service's status, or why there is none yet.
    var statusLine: String {
        let text = status.text
        return text.isEmpty ? "Waits for the Desk to be on." : text
    }

    /// The switch or the Desk changed: start or end the watch. Idempotent.
    func refresh() { service.refresh() }

    /// Follows the setting from here on.
    func startForApp() {
        observer = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: defaults, queue: .main
        ) { [weak self] _ in MainActor.assumeIsolated { self?.refresh() } }
        refresh()
    }
}
