import Foundation
import Observation

/// Claude's suggested Desk messages (item 54), the file half. The MCP server's
/// `propose_desk_messages` drops desk-messages-request.json into Sanduhr's Application Support
/// folder; this picks it up (HandoffWatch: one kernel watch on the folder, shared with the theme
/// handoff, no polling), checks it again
/// (MessageProposal) and either applies it, when Settings, Message's "Let Claude change the
/// messages directly" is on, or holds it as the suggestion Settings, Message shows with Add,
/// Review and Dismiss. Each step answers in desk-messages-result.json, which the server reads:
/// `pending_approval` at once, then `applied` or `rejected` when the user decides.
///
/// A suggestion waits in memory: quitting Sanduhr drops it (the server's caller was already told
/// it is pending). A newer request replaces an older one still waiting. The request file is
/// removed as soon as it is read. Applying keeps the previous file as messages.txt.previous.
/// Nothing here logs message text.
@MainActor
@Observable
final class DeskMessageHandoff {
    static let shared = DeskMessageHandoff()

    /// Settings, Message: "Let Claude change the messages directly", off by default.
    nonisolated static let directKey = "messageClaudeDirect"

    struct Paths {
        /// ~/Library/Application Support/Sanduhr: the request, result and state files.
        var support: URL
        /// Desk's messages.txt.
        var messages: URL

        static var standard: Paths {
            Paths(support: HandoffFiles.support, messages: MessageEngine.fileURL)
        }

        var request: URL { support.appendingPathComponent(MessageProposal.requestFile) }
        var result: URL { support.appendingPathComponent(MessageProposal.resultFile) }
        var state: URL { support.appendingPathComponent(MessageProposal.stateFile) }
        var backup: URL { messages.appendingPathExtension("previous") }
    }

    /// The suggestion waiting for the user, if any.
    private(set) var pending: MessageProposal?
    /// Goes up each time messages.txt is changed here, so an open editor reloads.
    private(set) var revision = 0

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let direct: () -> Bool
    @ObservationIgnored private let now: () -> Date
    /// A suggestion arrived and waits (the banner decision is the caller's).
    @ObservationIgnored var onSuggestion: ((MessageProposal) -> Void)?
    /// messages.txt changed here (the Desk picks today's line again).
    @ObservationIgnored var onApplied: (() -> Void)?
    /// A suggestion was added, dismissed or replaced by a newer one (its notification can go).
    @ObservationIgnored var onDecided: ((MessageProposal) -> Void)?
    /// Any change in Sanduhr's folder, after the request check (the app re-reports its settings).
    @ObservationIgnored var onFolderEvent: (() -> Void)?
    @ObservationIgnored private var watching = false
    @ObservationIgnored private var lastState: Data?
    @ObservationIgnored fileprivate var settingsObserver: NSObjectProtocol?

    init(paths: Paths = .standard,
         direct: @escaping () -> Bool = { UserDefaults.desk.bool(forKey: DeskMessageHandoff.directKey) },
         now: @escaping () -> Date = Date.init) {
        self.paths = paths
        self.direct = direct
        self.now = now
    }

    /// Watches Sanduhr's folder for requests (the shared HandoffWatch) and takes one already
    /// waiting from before launch.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in
            self?.check()
            self?.onFolderEvent?()
        }
        check()
    }

    /// Reads and removes a waiting request, if there is one.
    func check() {
        guard let data = HandoffFiles.take(paths.request) else { return }
        receive(data)
    }

    /// One request's bytes, as read from the file.
    func receive(_ data: Data) {
        switch MessageProposal.decode(data, now: now()) {
        case .discard:
            return
        case .refused(let id, let reasons):
            writeResult(id: id, status: .rejected, mode: nil, reasons: reasons)
        case .proposal(let p):
            if direct() {
                apply(p)
            } else {
                if let old = pending { onDecided?(old) }
                pending = p
                writeResult(id: p.id, status: .pendingApproval, mode: p.mode)
                onSuggestion?(p)
            }
        }
    }

    /// Add: the waiting suggestion goes into messages.txt.
    func approve() {
        guard let p = pending else { return }
        pending = nil
        onDecided?(p)
        apply(p)
    }

    /// Dismiss: the suggestion is dropped and the server's caller hears so.
    func dismiss() {
        guard let p = pending else { return }
        pending = nil
        onDecided?(p)
        writeResult(id: p.id, status: .rejected, mode: p.mode, reasons: ["dismissed by the user"])
    }

    private func apply(_ p: MessageProposal) {
        let fm = FileManager.default
        let had = fm.fileExists(atPath: paths.messages.path)
        let existing: String
        if had {
            guard let data = try? Data(contentsOf: paths.messages), let text = String(data: data, encoding: .utf8) else {
                writeResult(id: p.id, status: .rejected, mode: p.mode,
                            reasons: ["messages.txt could not be read as UTF-8; it was left alone"])
                return
            }
            existing = text
        } else {
            existing = ""
        }
        guard let applied = MessageProposal.apply(p, to: existing) else {
            writeResult(id: p.id, status: .rejected, mode: p.mode,
                        reasons: ["the list would pass \(MessageProposal.maxFileLines) lines"])
            return
        }
        do {
            try fm.createDirectory(at: paths.messages.deletingLastPathComponent(), withIntermediateDirectories: true)
            if had { try Data(existing.utf8).write(to: paths.backup, options: .atomic) }
            try Data(applied.text.utf8).write(to: paths.messages, options: .atomic)
        } catch {
            writeResult(id: p.id, status: .rejected, mode: p.mode, reasons: ["messages.txt could not be written"])
            return
        }
        revision += 1
        onApplied?()
        writeResult(id: p.id, status: .applied, mode: p.mode, applied: applied)
    }

    private func writeResult(id: String, status: MessageProposal.Status, mode: MessageProposal.Mode?,
                             reasons: [String] = [], applied: MessageProposal.Applied? = nil) {
        let data = MessageProposal.resultJSON(id: id, status: status, mode: mode, reasons: reasons,
                                              applied: applied, now: now())
        HandoffFiles.writeOwnerOnly(data, to: paths.result)
    }

    /// desk-messages-state.json, for get_desk_messages: rewritten only when the pin or the
    /// rotation changed.
    func writeState(pinned: String?, rotate: String) {
        let data = MessageProposal.stateJSON(pinned: pinned, rotate: rotate)
        guard data != lastState else { return }
        lastState = data
        HandoffFiles.writeOwnerOnly(data, to: paths.state)
    }
}

extension DeskMessageHandoff {
    /// The app's wiring: the Desk picks today's line again after a change, a suggestion may post a
    /// notification (Notifier), and desk-messages-state.json follows the pin and the rotation
    /// (rewritten only when they change: on any in-process defaults change, and on any event in
    /// Sanduhr's folder, which catches a `defaults write` within the snapshot's five minutes).
    func startForApp() {
        onApplied = {
            let model = DeskController.shared.model
            if !model.demo { model.message = MessageEngine.current() }
        }
        onSuggestion = { Notifier.shared.suggestDeskMessages($0) }
        onDecided = { Notifier.shared.clearDeskSuggestion($0) }
        onFolderEvent = { [weak self] in self?.reportSettings() }
        reportSettings()
        settingsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in MainActor.assumeIsolated { self?.reportSettings() } }
        start()
    }

    func reportSettings() {
        let d = UserDefaults.desk
        writeState(pinned: d.string(forKey: "message"), rotate: d.string(forKey: "messageRotate") ?? "daily")
    }
}
