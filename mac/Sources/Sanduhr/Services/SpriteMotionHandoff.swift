import Foundation
import Observation

/// The app's half of `propose_sprite_motion` (spec 2.39.0 section 19), the file layer only: a port of the shape of
/// `WidgetProposalHandoff`. The MCP server drops `sprite-motion-request.json` into Sanduhr's Application Support
/// folder; this takes it through the shared `HandoffWatch`, checks it again (`SpriteMotionProposal.decode`: the app
/// never trusts the server) and holds a good one in memory only. Every request is answered in
/// `sprite-motion-result.json` (atomic, owner only); the request file is removed as soon as it is read and one older
/// than ten minutes is dropped unanswered.
///
/// - A good proposal is held as `pending` and answered `previewing` once `isReady` says the review can be shown;
///   until then it is answered `rejected`, `not_ready`. The Settings banner (`model`) offers **Preview on the Desk**,
///   **Apply** and **Decline**.
/// - **Preview on the Desk** draws the sprite with the proposed motion, from memory: nothing is written, the saved
///   companion choice is untouched. Decline, Apply and a newer request take it down.
/// - **Apply** rewrites the installed sprite (`SpriteMotionReview.apply`) and answers `applied`; a sprite uninstalled
///   meanwhile, or a pack that no longer validates, answers `rejected`, `install_failed` with a sentence. **Decline**
///   answers `rejected`, `dismissed` and changes nothing.
/// - A newer request answers the older one `rejected`, `replaced`. One older than ten minutes is dropped unanswered
///   (the same window as widgets; there is no `expired` status).
/// - Nothing logs a motion's content.
@MainActor
@Observable
final class SpriteMotionHandoff {
    static let shared = SpriteMotionHandoff()

    struct Paths {
        /// ~/Library/Application Support/Sanduhr: the request and result files, as `Paths.support_dir` in the MCP.
        var support: URL

        static var standard: Paths { Paths(support: HandoffFiles.support) }

        var request: URL { support.appendingPathComponent(SpriteMotionProposal.requestFile) }
        var result: URL { support.appendingPathComponent(SpriteMotionProposal.resultFile) }
    }

    /// A motion waiting for the user: the request id, the sprite, the fields Claude sent, the pack's files with the
    /// new `sprite.json` (validated, not installed) and Claude's note.
    struct Review {
        let id: String
        let spriteId: String
        let motion: PackObject
        let files: [String: Data]
        let note: String?
        let name: String
        /// "What changes", worked out once when the request arrived.
        let changes: [String]
    }

    /// The motion waiting for the user, if any. In memory only: quitting Sanduhr drops it.
    private(set) var pending: Review?
    /// The pending motion is on the Desk now (Preview on the Desk).
    private(set) var previewing = false
    /// One sentence when Preview on the Desk could not show it, else nil.
    private(set) var previewNote: String?

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let storeFor: () -> WidgetStore
    @ObservationIgnored private let now: () -> Date
    /// The sprite is read and rewritten through this (the store, or a fake in tests).
    @ObservationIgnored var motionStore: (() -> SpriteMotionStoring)?
    /// The Desk side of a preview; nil previews nothing (tests, a build with no Desk).
    @ObservationIgnored var host: WidgetPreviewHost?
    /// Called just before the Desk shows this preview, so another preview can come down.
    @ObservationIgnored var willPreview: (() -> Void)?
    /// The motion was applied, declined or replaced (its notification can go).
    @ObservationIgnored var onDecided: ((String) -> Void)?
    /// The sprite was rewritten: the companion and the lists follow.
    @ObservationIgnored var onInstalled: ((String) -> Void)?
    /// Whether a review can be shown now. False answers a good proposal `not_ready`.
    @ObservationIgnored var isReady: () -> Bool = { false }
    /// A proposal arrived and waits (the notification is the caller's).
    @ObservationIgnored var onPending: ((SpriteMotionReviewModel) -> Void)?
    /// Every answer written (a test seam: the result file holds only the last).
    @ObservationIgnored var onAnswered: ((String, WidgetAnswer) -> Void)?
    @ObservationIgnored private var watching = false

    init(paths: Paths = .standard, store: @escaping () -> WidgetStore = { .shared }, now: @escaping () -> Date = Date.init) {
        self.paths = paths
        self.storeFor = store
        self.now = now
    }

    /// Watches Sanduhr's folder (the shared HandoffWatch) and takes a request already waiting.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    /// Reads and removes a waiting request, if there is one.
    func check() {
        guard let data = HandoffFiles.take(paths.request, maxBytes: SpriteMotionProposal.maxRequestBytes) else { return }
        receive(data)
    }

    /// One request's bytes, as read from the file.
    func receive(_ data: Data) {
        let request = SpriteMotionProposal.decode(data, now: now(), store: storeFor())
        switch request.kind {
        case .discard:
            return
        case .refused:
            answer(request.id!, .refused(request.reasons))
        case .proposal:
            answer(request.id!, take(request))
        }
    }

    /// What the banner shows for the pending motion, nil with none.
    var model: SpriteMotionReviewModel? {
        guard let p = pending else { return nil }
        return SpriteMotionReviewModel(
            id: p.id, spriteId: p.spriteId, name: p.name, heading: SpriteMotionReviewCopy.heading(p.name),
            note: p.note.flatMap { $0.unicodeScalars.contains { !$0.properties.isWhitespace } ? $0 : nil },
            changesTitle: SpriteMotionReviewCopy.changesTitle, changes: p.changes + [SpriteMotionReviewCopy.unchangedArt],
            deskOff: !(host?.deskRunning ?? false), previewing: previewing)
    }

    /// Preview on the Desk: the sprite as the companion with the proposed motion, for now. False (and `previewNote`
    /// says why) when the Desk is off or the sprite can't be drawn. Nothing is written.
    @discardableResult
    func previewOnDesk() -> Bool {
        guard let p = pending, let host else { return false }
        guard host.deskRunning else { previewNote = WidgetReviewCopy.deskOff; return false }
        guard let companion = CompanionPreview.make(packId: p.spriteId, files: p.files) else {
            previewNote = "This sprite could not be drawn, so there is nothing to preview. Nothing was changed."
            return false
        }
        willPreview?()
        host.show(WidgetDeskPreview(companion: companion, ink: nil))
        previewing = true
        previewNote = nil
        return true
    }

    /// Takes the preview down: the Desk is as it was. Nothing is answered (the motion still waits).
    func endPreview() {
        guard previewing else { return }
        previewing = false
        host?.end()
    }

    /// Decline: nothing is changed, the preview goes, and the caller is told.
    func decline() {
        guard let p = pending else { return }
        close(p)
        answer(p.id, WidgetAnswer(WidgetAnswer.rejected, reason: "dismissed",
                                  reasons: [DeskReason("sprite", "declined by the user")], packId: p.spriteId))
    }

    /// Apply: rewrites the installed sprite with the motion. The caller is told either way.
    func apply() {
        guard let p = pending else { return }
        close(p)
        let result = SpriteMotionReview.apply(spriteId: p.spriteId, motion: p.motion, store: motionStore?() ?? storeFor())
        if result.status == WidgetAnswer.applied { onInstalled?(p.spriteId) }
        answer(p.id, result)
    }

    private func close(_ p: Review) {
        pending = nil
        endPreview()
        previewNote = nil
        onDecided?(p.id)
    }

    private func take(_ request: SpriteMotionRequest) -> WidgetAnswer {
        let spriteId = request.spriteId!
        guard !ResetGate.isOn, isReady() else { return Self.notReady(spriteId) }
        if let old = pending {
            close(old)
            answer(old.id, WidgetAnswer(WidgetAnswer.rejected, reason: "replaced",
                                        reasons: [DeskReason("sprite", "replaced by a newer proposal")], packId: old.spriteId))
        }
        let files = request.files!
        let installed = storeFor().readFiles(spriteId)?["sprite.json"]
        let name = storeFor().readInfo(spriteId)?.name ?? spriteId
        let review = Review(id: request.id!, spriteId: spriteId, motion: request.motion!, files: files, note: request.note,
                            name: name, changes: SpriteMotionReview.changes(old: installed, new: files["sprite.json"] ?? Data()))
        pending = review
        previewNote = nil
        if let m = model { onPending?(m) }
        return WidgetAnswer(WidgetAnswer.previewing, packId: spriteId)
    }

    private static func notReady(_ spriteId: String) -> WidgetAnswer {
        WidgetAnswer(WidgetAnswer.rejected, reason: "not_ready",
                     reasons: [DeskReason("sprite", "not_ready: this version of Sanduhr checks sprite motion but cannot show it here")],
                     packId: spriteId)
    }

    private func answer(_ id: String, _ answer: WidgetAnswer) {
        onAnswered?(id, answer)
        HandoffFiles.writeOwnerOnly(SpriteMotionProposal.resultJson(id: id, answer: answer, now: now()), to: paths.result)
    }
}

extension SpriteMotionHandoff {
    /// The app's wiring: the Desk preview host, the suggestion notification, and an applied motion reloads what
    /// lists packs (the companion redraws with the new motion).
    func startForApp() {
        host = DeskWidgetPreviewHost()
        isReady = { true }
        willPreview = { WidgetProposalHandoff.shared.endPreview() }
        onPending = { Notifier.shared.suggestSpriteMotion($0) }
        onDecided = { Notifier.shared.clearSpriteMotion(id: $0) }
        onInstalled = { _ in
            WidgetsModel.refreshPackLists()
            NotificationCenter.default.post(name: .widgetPacksDidChange, object: self)
        }
        start()
    }
}
