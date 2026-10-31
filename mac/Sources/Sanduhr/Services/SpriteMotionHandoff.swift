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
///   until then it is answered `rejected`, `not_ready`. The review banner, **Preview on the Desk** and **Apply**
///   are not here: whoever builds them reads `pending`, rewrites the sprite, and closes it with `finish`.
/// - A newer request answers the older one `rejected`, `replaced`.
/// - Nothing here changes a sprite or draws anything. Nothing logs a motion's content.
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
    }

    /// The motion waiting for the user, if any. In memory only: quitting Sanduhr drops it.
    private(set) var pending: Review?

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let storeFor: () -> WidgetStore
    @ObservationIgnored private let now: () -> Date
    /// Whether a review can be shown now. False answers a good proposal `not_ready` (the banner is a later layer).
    @ObservationIgnored var isReady: () -> Bool = { false }
    /// A proposal arrived and waits.
    @ObservationIgnored var onPending: ((Review) -> Void)?
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

    /// The review is over (applied, dismissed or failed): the pending motion goes and the caller is told.
    func finish(_ result: WidgetAnswer) {
        guard let p = pending else { return }
        pending = nil
        answer(p.id, result)
    }

    private func take(_ request: SpriteMotionRequest) -> WidgetAnswer {
        let spriteId = request.spriteId!
        guard !ResetGate.isOn, isReady() else { return Self.notReady(spriteId) }
        if let old = pending {
            pending = nil
            answer(old.id, WidgetAnswer(WidgetAnswer.rejected, reason: "replaced",
                                        reasons: [DeskReason("sprite", "replaced by a newer proposal")], packId: old.spriteId))
        }
        let review = Review(id: request.id!, spriteId: spriteId, motion: request.motion!, files: request.files!, note: request.note)
        pending = review
        onPending?(review)
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
