import Foundation
import Observation

/// Watchers (item 66), the file half: picks up the MCP server's requests and the Stop hook's
/// reports from Sanduhr's Application Support folder (HandoffWatch: one kernel watch on the
/// folder, shared with the other handoffs, no polling), deletes each file as it reads it, and
/// keeps the board in memory. Nothing here logs a title, a note or a description, and nothing is
/// written but `watchers.json`, the two switches the server and the hook read:
///
///     { "agents": true, "background": false, "schema_version": 1 }
///
/// With "Let agents show watchers" off, an agent's request is deleted unread and the server
/// refuses before writing one; with "Show Claude Code's background work" off the hook writes
/// nothing, and a report that slipped in is deleted unread. Turning a switch off clears its
/// watchers. Quitting Sanduhr drops every watcher. The one other file is the band's (BandFile),
/// written only while "Show watchers above the prompt" is on, with an agent's watcher's title and
/// short title and a background task's kind, never a note or a description.
@MainActor
@Observable
final class WatcherStore {
    static let shared = WatcherStore.standard()

    /// Settings, Integrations, Watchers: "Let agents show watchers", off by default.
    nonisolated static let agentsKey = "watchersAgents"
    /// Settings, Integrations, Watchers: "Show Claude Code's background work", off by default.
    nonisolated static let backgroundKey = "watchersBackground"
    /// The switches as the server and the hook read them. The server mirrors these names
    /// (sanduhr_mcp.py); a test on each side pins them.
    nonisolated static let switchFile = "watchers.json"
    nonisolated static let requestPrefix = "watch-request-"
    nonisolated static let stopPrefix = "watch-stop-"

    private(set) var board = WatcherBoard()

    @ObservationIgnored let support: URL
    @ObservationIgnored private let agents: () -> Bool
    @ObservationIgnored private let background: () -> Bool
    /// Whether a Claude Code folder (nil: the default) is linked to a work account.
    @ObservationIgnored private let isWork: (String?) -> Bool
    @ObservationIgnored private let now: () -> Date
    /// The board changed (the Desk model takes the ordered list).
    @ObservationIgnored var onChange: (() -> Void)?
    /// A watcher started waiting on you (the notch glows once, when it shows).
    @ObservationIgnored var onWaiting: ((Watcher) -> Void)?
    @ObservationIgnored private var watching = false
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var lastSwitches: (agents: Bool, background: Bool)?
    @ObservationIgnored fileprivate var settingsObserver: NSObjectProtocol?

    init(support: URL = HandoffFiles.support,
         agents: @escaping () -> Bool = { UserDefaults.standard.bool(forKey: WatcherStore.agentsKey) },
         background: @escaping () -> Bool = { UserDefaults.standard.bool(forKey: WatcherStore.backgroundKey) },
         isWork: @escaping (String?) -> Bool = { _ in false },
         now: @escaping () -> Date = Date.init) {
        self.support = support
        self.agents = agents
        self.background = background
        self.isWork = isWork
        self.now = now
    }

    /// Most urgent first (WatcherBoard.ordered).
    var ordered: [Watcher] { board.ordered() }

    /// Watches Sanduhr's folder (the shared HandoffWatch) and takes what already waits.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in self?.check() }
        applySwitches()
        check()
    }

    /// Reads and deletes every waiting report and request, oldest first (the names start with
    /// the time they were made). Reports first: a Stop's list is the session's state, a request a
    /// step.
    func check() {
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: support.path) else { return }
        for prefix in [Self.stopPrefix, Self.requestPrefix] {
            for name in names.filter({ $0.hasPrefix(prefix) && $0.hasSuffix(".json") }).sorted() {
                let url = support.appendingPathComponent(name)
                // A Stop's report is the session's state at that moment: one older than a request
                // may be (written while no Sanduhr read it, and only an older hook writes then)
                // is stale, and is deleted unread.
                if prefix == Self.stopPrefix, Self.isStale(url, now: now()) {
                    try? FileManager.default.removeItem(at: url)
                    continue
                }
                guard let data = HandoffFiles.take(url,
                                                   maxBytes: WatcherRequest.maxBytes) else { continue }
                if prefix == Self.stopPrefix { receiveStop(data) } else { receiveRequest(data) }
            }
        }
    }

    /// Last written more than WatcherRequest.maxAge before `now`.
    nonisolated static func isStale(_ url: URL, now: Date) -> Bool {
        guard let m = (try? FileManager.default.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date
        else { return false }
        return now.timeIntervalSince(m) > WatcherRequest.maxAge
    }

    /// One agent request's bytes. Dropped unread while the switch is off, unless `force` (the
    /// debug actions, which post a made-up watcher whatever the switches say).
    func receiveRequest(_ data: Data, force: Bool = false) {
        guard force || agents(), let d = WatcherRequest.decode(data, now: now()) else { return }
        var command = d.command
        if case let .start(id, title, link, total, work, short) = command, !work, isWork(d.folder) {
            command = .start(id: id, title: title, link: link, total: total, work: true, short: short)
        }
        let waiting = board.apply(command, now: now())
        changed()
        if waiting, case let .update(id, _, _, _) = command, let w = board.watcher(WatcherBoard.agentID(id)) {
            onWaiting?(w)
        }
    }

    /// One Stop's report. Dropped unread while the switch is off, unless `force`.
    func receiveStop(_ data: Data, force: Bool = false) {
        guard force || background(), let report = WatcherRequest.decodeStop(data) else { return }
        if board.applyStop(report, work: isWork(report.folder), now: now()) { changed() }
    }

    func dismiss(_ id: String) {
        board.dismiss(id)
        changed()
    }

    func dismissAll() {
        board.dismissAll()
        changed()
    }

    /// The switches changed (or at start): a switch turned off clears its watchers, and
    /// watchers.json follows. Unchanged switches do nothing.
    func applySwitches() {
        let current = (agents: agents(), background: background())
        if let last = lastSwitches, last == current { return }
        lastSwitches = current
        var cleared = false
        if !current.agents, board.watchers.contains(where: { $0.source == .agent }) {
            board.clear(.agent); cleared = true
        }
        if !current.background, board.watchers.contains(where: { $0.source == .automatic }) {
            board.clear(.automatic); cleared = true
        }
        HandoffFiles.writeOwnerOnly(Self.switchesJSON(agents: current.agents, background: current.background),
                                    to: support.appendingPathComponent(Self.switchFile))
        if cleared { changed() }
    }

    /// watchers.json's bytes: sorted keys and no spaces, so the hook's plain text match on
    /// `"background":true` holds.
    nonisolated static func switchesJSON(agents: Bool, background: Bool) -> Data {
        let o: [String: Any] = ["agents": agents, "background": background, "schema_version": 1]
        return (try? JSONSerialization.data(withJSONObject: o, options: [.sortedKeys])) ?? Data()
    }

    /// The clock moved: lost touch and fades (WatcherBoard.tick).
    func tick() {
        if board.tick(now: now()) { onChange?() }
        schedule()
    }

    private func changed() {
        onChange?()
        schedule()
    }

    /// One timer, set for the next moment the board changes on its own; none while nothing waits.
    private func schedule() {
        timer?.invalidate()
        timer = nil
        guard let next = board.nextChange() else { return }
        let t = Timer(timeInterval: max(0.2, next.timeIntervalSince(now())), repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }
}

extension WatcherStore {
    /// Whether `folder` (nil: the default `~/.claude`) is linked to an account marked work in
    /// Settings, Accounts, Data.
    nonisolated static func isWorkFolder(_ folder: String?, home: String, in store: DefaultsStore) -> Bool {
        let path = folder ?? (home as NSString).appendingPathComponent(".claude")
        guard let account = AccountData.account(linkedTo: path, in: store) else { return false }
        return AccountData.choices(for: account, in: store).work
    }

    /// The app's wiring: the Desk model takes the ordered watchers, a watcher waiting on you
    /// glows the notch once when watchers show somewhere, and the switches follow Settings.
    func startForApp() {
        onChange = { [weak self] in
            guard let self else { return }
            DeskController.shared.model.setWatchers(self.ordered)
            // The band above Claude Code's prompt (band.json), while its switch is on.
            BandFileWriter.shared.refresh()
        }
        onWaiting = { w in
            let desk = UserDefaults.desk
            guard DeskController.shared.running, !WatcherPlacement.places(in: desk).isEmpty,
                  !WatcherBoard.shown([w], demo: DeskController.shared.model.demo).isEmpty else { return }
            NotchGlowController.shared.fire(topFallback: true)
        }
        settingsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in MainActor.assumeIsolated { self?.applySwitches() } }
        start()
    }

    /// The smoke tools' made-up watcher (`smoke do watch-test …`): through the same decoding as
    /// an agent's request, whatever the switches say.
    static let testID = "smoke-test"

    func debug(_ test: DebugAction.WatchTest) {
        let at = now()
        let command: WatcherCommand
        switch test {
        case .start:
            command = .start(id: Self.testID, title: "Smoke watcher", link: URL(string: "https://example.com"),
                             total: 10, work: false, short: "smoke")
        case .wait: command = .update(id: Self.testID, done: 4, note: "needs a look", state: .waiting)
        case .pass: command = .end(id: Self.testID, result: .passed, note: "all green")
        case .fail: command = .end(id: Self.testID, result: .failed, note: "2 checks failed")
        case .clear:
            dismissAll()
            return
        }
        receiveRequest(WatcherRequest.json(command, at: at), force: true)
    }

    /// The app's store: the account links decide the work tag.
    static func standard() -> WatcherStore {
        WatcherStore(isWork: { folder in
            WatcherStore.isWorkFolder(folder, home: NSHomeDirectory(), in: KeychainStore.accounts.defaults)
        })
    }
}

/// Where watchers show: notch places set to Watchers (while the island and its text are on) and
/// the Desk layout's "watchers" element. For the glow decision and state.yaml.
enum WatcherPlacement {
    /// The Desk layout's key.
    static let widget = "watchers"
    /// Most watchers the Desk stack draws.
    static let deskRows = 4

    /// "left", "right", "strip" and "desk", in that order, where watchers are placed now.
    static func places(notch: Bool, wingText: Bool, chinText: Bool, chin: Double,
                       left: NotchContent, right: NotchContent, strip: NotchContent,
                       layoutPlaced: Set<String>) -> [String] {
        var out: [String] = []
        if notch, wingText, left == .watchers { out.append("left") }
        if notch, wingText, right == .watchers { out.append("right") }
        if notch, chinText, chin > 0, strip == .watchers { out.append("strip") }
        if layoutPlaced.contains(widget) { out.append("desk") }
        return out
    }

    static func places(in d: UserDefaults) -> [String] {
        places(notch: d.bool(forKey: DeskController.notchKey),
               wingText: d.object(forKey: "notchText") as? Bool ?? true,
               chinText: d.bool(forKey: "notchChinText"),
               chin: d.object(forKey: "notchChin") as? Double ?? 26,
               left: NotchContent.saved(.left, in: d), right: NotchContent.saved(.right, in: d),
               strip: NotchContent.saved(.strip, in: d),
               layoutPlaced: DeskLayout.placed(d.string(forKey: "layout") ?? DeskLayout.standard))
    }

    // MARK: Settings, Watchers (Settings v2, slice 2)

    /// Watchers, Where they show, On the notch: Off or one place. It reads and writes the notch
    /// places' own keys, the ones the Notch page's pickers write.
    enum NotchSpot: String, CaseIterable, Identifiable {
        case off, left, right, strip

        var id: String { rawValue }

        var label: String {
            switch self {
            case .off: "Off"
            case .left: "Left wing"
            case .right: "Right wing"
            case .strip: "Under the camera"
            }
        }

        var place: NotchContent.Place? {
            switch self {
            case .off: nil
            case .left: .left
            case .right: .right
            case .strip: .strip
            }
        }

        static func of(_ place: NotchContent.Place) -> NotchSpot {
            switch place {
            case .left: .left
            case .right: .right
            case .strip: .strip
            }
        }
    }

    static let notchPlaces: [NotchContent.Place] = [.left, .right, .strip]

    /// The first notch place set to Watchers, or Off.
    static func notchSpot(in d: DefaultsStore) -> NotchSpot {
        notchPlaces.first { NotchContent.resolve($0, raw: d.object(forKey: $0.key) as? String) == .watchers }
            .map(NotchSpot.of) ?? .off
    }

    /// Puts watchers in `spot`: any other place set to Watchers goes back to its default content.
    static func setNotchSpot(_ spot: NotchSpot, in d: DefaultsStore) {
        for place in notchPlaces where place != spot.place
            && NotchContent.resolve(place, raw: d.object(forKey: place.key) as? String) == .watchers {
            d.set(nil, forKey: place.key)
        }
        if let place = spot.place { d.set(NotchContent.watchers.rawValue, forKey: place.key) }
    }

    /// Why the notch place picked doesn't show watchers now, nil when it does (or none is picked).
    static func notchHint(spot: NotchSpot, notch: Bool, wingText: Bool, chinText: Bool, chin: Double) -> String? {
        guard spot != .off else { return nil }
        if !notch { return "The notch is off, so watchers don't show there." }
        if spot == .strip, !chinText || chin == 0 {
            return "Text under the camera is off or has no height, so watchers don't show there."
        }
        if spot != .strip, !wingText { return "Text beside the camera is off, so watchers don't show there." }
        return nil
    }

    static func notchHint(in d: UserDefaults) -> String? {
        notchHint(spot: notchSpot(in: d), notch: d.bool(forKey: DeskController.notchKey),
                  wingText: d.object(forKey: "notchText") as? Bool ?? true,
                  chinText: d.bool(forKey: "notchChinText"),
                  chin: d.object(forKey: "notchChin") as? Double ?? 26)
    }

    /// The rule `WatcherStore.startForApp` follows, as the Watchers page states it.
    static let glowRule = "The notch glows once when a watcher waits on you, if watchers show somewhere above."

    /// Where `places` are, in words: "on the right wing and the Desk".
    static func placesText(_ places: [String]) -> String {
        let words = places.map { p -> String in
            switch p {
            case "left": "the left wing"
            case "right": "the right wing"
            case "strip": "under the camera"
            default: "the Desk"
            }
        }
        let joined = words.count > 1 ? words.dropLast().joined(separator: ", ") + " and " + words.last! : words.first ?? ""
        return joined.hasPrefix("under") ? joined : "on " + joined
    }

    /// Whether the rule holds now, and why not when it doesn't.
    static func glowStatus(deskOn: Bool, places: [String]) -> String {
        if !deskOn { return "Now: the Desk is off, so watchers don't show and a waiting watcher doesn't glow the notch." }
        if places.isEmpty {
            return "Now: watchers aren't placed anywhere they can show, so a waiting watcher doesn't glow the notch. Place them above."
        }
        return "Now: watchers show \(placesText(places)), so one that waits on you glows the notch once."
    }

    /// A folder's meters mod, for Above the prompt.
    static func metersLine(_ status: IntegrationStatus) -> String {
        switch status {
        case .installed: "Meters mod installed"
        case .outdated: "Meters mod installed, update waiting"
        case .notInstalled, .other: "No meters mod"
        case .unreadable: "settings.json isn't valid JSON"
        }
    }
}
