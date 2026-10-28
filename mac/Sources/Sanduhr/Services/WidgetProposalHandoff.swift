import Foundation
import Observation

/// Widgets made with Claude (spec "Claude-made widgets"), the app's half of `propose_widget`; a port of Windows'
/// `WidgetHandoff` and `WidgetReviewService`. The MCP server drops `widget-request.json` into Sanduhr's Application
/// Support folder; this takes it through the shared `HandoffWatch`, checks the pack again with the widget's own rules
/// (`WidgetProposal.decode`: the app never trusts the server), and holds a good one in memory only. Every request is
/// answered in `widget-result.json` (atomic, owner only). The request file is removed as soon as it is read, and one
/// older than ten minutes is dropped unanswered.
///
/// - A good proposal is held and answered `previewing`; the Settings banner (the pending proposal and its
///   `WidgetReviewModel`) offers **Preview on the Desk**, **Make it the companion** (a sprite), **Apply** and **Dismiss**.
///   Nothing is installed, and nothing is drawn on the Desk, until the user acts.
/// - **Preview on the Desk** puts a sprite on the Desk as the companion, or a palette as the Desk ink, for now: from
///   memory, with the saved companion choice and ink untouched and nothing in the widgets folder. Dismiss, Apply and a
///   newer request take it down. Effects packs have nothing to draw, so there is nothing to preview.
/// - **Apply** installs the pack through `WidgetStore.install(source: .claude)` (a widget made here) and answers
///   `applied` (`companion` says whether a sprite became the companion); a store failure answers `rejected`,
///   `install_failed`. **Dismiss** answers `rejected`, `dismissed`.
/// - A newer request answers the older one `rejected`, `replaced`. `busy` while the Desk is being arranged; `not_ready`
///   with no Desk wired or after Reset Sanduhr… ran.
/// - No setting lets Claude skip Apply for a widget. Nothing here logs a pack's name or content.
@MainActor
@Observable
final class WidgetProposalHandoff {
    static let shared = WidgetProposalHandoff()

    struct Paths {
        /// ~/Library/Application Support/Sanduhr: the request and result files, as `Paths.support_dir` in the MCP.
        var support: URL

        static var standard: Paths { Paths(support: HandoffFiles.support) }

        var request: URL { support.appendingPathComponent(WidgetProposal.requestFile) }
        var result: URL { support.appendingPathComponent(WidgetProposal.resultFile) }
    }

    /// A proposal waiting for the user: the request id, the pack Sanduhr checked, Claude's note.
    struct Review {
        let id: String
        let pack: WidgetProposalPack
        let note: String?
    }

    /// The proposal waiting for the user, if any. In memory only: quitting Sanduhr drops it.
    private(set) var pending: Review?
    /// The pending proposal is on the Desk now (Preview on the Desk).
    private(set) var previewing = false
    /// One sentence when Preview on the Desk could not show it (the Desk is off, an effects pack), else nil.
    private(set) var previewNote: String?

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let storeFor: () -> WidgetStore
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let now: () -> Date
    /// The Desk side of a review; nil answers a proposal `not_ready` (tests, a build with no Desk).
    @ObservationIgnored var host: WidgetPreviewHost?
    /// A proposal arrived and waits (the notification is the caller's).
    @ObservationIgnored var onSuggestion: ((WidgetReviewModel) -> Void)?
    /// The proposal was applied, dismissed or replaced (its notification can go).
    @ObservationIgnored var onDecided: ((String) -> Void)?
    /// A pack was installed: Settings ▸ Widgets, the companion and the palette lists follow.
    @ObservationIgnored var onInstalled: ((String) -> Void)?
    /// Every answer written (a test seam: the result file holds only the last).
    @ObservationIgnored var onAnswered: ((String, WidgetAnswer) -> Void)?
    @ObservationIgnored private var watching = false

    /// - Parameters:
    ///   - store: The widgets folder an Apply installs into (the app's, or a temp folder in tests).
    ///   - defaults: The Desk defaults suite (`CompanionSettings`, the layout).
    init(paths: Paths = .standard, store: @escaping () -> WidgetStore = { .shared },
         defaults: UserDefaults = .desk, now: @escaping () -> Date = Date.init) {
        self.paths = paths
        self.storeFor = store
        self.defaults = defaults
        self.now = now
    }

    // MARK: - The file half

    /// Watches Sanduhr's folder (the shared HandoffWatch) and takes a request already waiting.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    /// Reads and removes a waiting request, if there is one.
    func check() {
        guard let data = HandoffFiles.take(paths.request, maxBytes: WidgetProposal.maxRequestBytes) else { return }
        receive(data)
    }

    /// One request's bytes, as read from the file.
    func receive(_ data: Data) {
        let request = WidgetProposal.decode(data, now: now())
        switch request.kind {
        case .discard:
            return
        case .refused:
            answer(request.id!, .refused(request.reasons))
        case .proposal:
            answer(request.id!, take(id: request.id!, pack: request.pack!, note: request.note))
        }
    }

    private func answer(_ id: String, _ answer: WidgetAnswer) {
        onAnswered?(id, answer)
        HandoffFiles.writeOwnerOnly(WidgetProposal.resultJson(id: id, answer: answer, now: now()), to: paths.result)
    }

    // MARK: - The review

    /// A pack that passed the widget's own check: held for the banner, never installed here. The direct switch is
    /// deliberately not read: widgets always wait for Apply.
    private func take(id: String, pack: WidgetProposalPack, note: String?) -> WidgetAnswer {
        guard !ResetGate.isOn, let host else { return .notReady }
        if host.deskBusy {
            return WidgetAnswer(WidgetAnswer.rejected, reason: "busy",
                reasons: [DeskReason("widget", "the Desk is busy (its layout is being arranged, or a Desk design waits for the user's review); ask them to finish or dismiss it, then propose the widget again")],
                packId: pack.id)
        }
        replacePending()
        pending = Review(id: id, pack: pack, note: note)
        previewNote = nil
        if let model { onSuggestion?(model) }
        return WidgetAnswer(WidgetAnswer.previewing, packId: pack.id)
    }

    /// A newer proposal takes the place of the one waiting: the older one is answered `replaced`.
    private func replacePending() {
        guard let old = pending else { return }
        pending = nil
        endPreview()
        onDecided?(old.id)
        answer(old.id, WidgetAnswer(WidgetAnswer.rejected, reason: "replaced",
                                    reasons: [DeskReason("widget", "replaced by a newer proposal")], packId: old.pack.id))
    }

    /// What the banner shows for the pending proposal, nil with none.
    var model: WidgetReviewModel? {
        guard let p = pending else { return nil }
        return WidgetReview.model(id: p.id, pack: p.pack, note: p.note, deskOn: host?.deskRunning ?? false, previewing: previewing)
    }

    /// Preview on the Desk: a sprite as the companion, a palette as the Desk ink, for now. False (and `previewNote`
    /// says why) when the Desk is off, there is nothing to draw, or the sprite cannot be drawn. A preview already up
    /// is replaced.
    @discardableResult
    func previewOnDesk() -> Bool {
        guard let p = pending, let host else { return false }
        guard p.pack.kind != "effects" else { previewNote = WidgetReviewCopy.noPreview; return false }
        guard host.deskRunning else { previewNote = WidgetReviewCopy.deskOff; return false }
        var preview = WidgetDeskPreview()
        if p.pack.kind == "sprite" {
            guard let companion = CompanionPreview.make(p.pack) else {
                previewNote = "This sprite could not be drawn, so there is nothing to preview. Nothing was installed."
                return false
            }
            preview.companion = companion
        } else {
            guard let ink = WidgetReview.previewInk(p.pack) else {
                previewNote = "This palette has no color to preview. Nothing was installed."
                return false
            }
            preview.ink = ink
        }
        host.show(preview)
        previewing = true
        previewNote = nil
        return true
    }

    /// Takes the preview down: the Desk is as it was. Nothing is answered (the proposal still waits).
    func endPreview() {
        guard previewing else { return }
        previewing = false
        host?.end()
    }

    /// Dismiss: nothing is installed, the preview goes, and the caller is told.
    func dismiss() {
        guard let p = pending else { return }
        pending = nil
        endPreview()
        previewNote = nil
        onDecided?(p.id)
        answer(p.id, WidgetAnswer(WidgetAnswer.rejected, reason: "dismissed",
                                  reasons: [DeskReason("widget", "dismissed by the user")], packId: p.pack.id))
    }

    /// Apply (and, for a sprite, "Make it the companion"): installs the pack as a widget made here. The caller is told
    /// either way.
    func apply(makeCompanion: Bool = false) {
        guard let p = pending else { return }
        pending = nil
        endPreview()
        previewNote = nil
        onDecided?(p.id)
        let result = install(p.pack, makeCompanion: makeCompanion)
        if result.status == WidgetAnswer.applied { onInstalled?(p.pack.id) }
        answer(p.id, result)
    }

    private func install(_ pack: WidgetProposalPack, makeCompanion: Bool) -> WidgetAnswer {
        // The pack is checked once more, then installed atomically.
        let check = WidgetProposal.check(pack.files, expectedId: pack.id)
        guard let checked = check.pack else { return .refused(check.reasons) }
        let result = storeFor().install(checked.files, checked.manifest, source: .claude)
        guard result.ok else {
            return WidgetAnswer(WidgetAnswer.rejected, reason: "install_failed",
                                reasons: [DeskReason("widget", result.error ?? "could not be installed")], packId: pack.id)
        }
        var companion: Bool?
        if pack.kind == "sprite" {
            companion = makeCompanion
            if makeCompanion { becomeCompanion(pack.id) }
        }
        return WidgetAnswer(WidgetAnswer.applied, packId: pack.id, companion: companion)
    }

    /// The saved companion is this pack, and the companion piece is placed on the Desk where it would go, so it shows.
    private func becomeCompanion(_ id: String) {
        CompanionSettings(pack: id).save(defaults)
        guard !ResetGate.isOn else { return }
        var layout = DeskArrangement(defaults.string(forKey: DeskArrangeMode.layoutKey) ?? DeskLayout.standard)
        if layout.placement("companion") == nil {
            layout.pieces.append(DeskPlacement(widget: "companion", anchor: DeskArrangement.defaultAnchor("companion")))
            defaults.set(layout.string, forKey: DeskArrangeMode.layoutKey)
        }
    }
}

extension WidgetProposalHandoff {
    /// The app's wiring: the Desk preview host, the suggestion notification, and an install reloads what lists
    /// packs (the message palettes, the companion).
    func startForApp() {
        host = DeskWidgetPreviewHost()
        onSuggestion = { Notifier.shared.suggestWidget($0) }
        onDecided = { Notifier.shared.clearWidgetSuggestion(id: $0) }
        onInstalled = { _ in
            WidgetsModel.refreshPackLists()
            NotificationCenter.default.post(name: .widgetPacksDidChange, object: self)
        }
        start()
    }
}
