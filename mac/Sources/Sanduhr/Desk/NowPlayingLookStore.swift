import Foundation
import Observation

/// Per-song looks for now playing (item 65c), the file and memory half. The MCP server's
/// `propose_now_playing_looks` drops now-playing-looks-request.json into Sanduhr's folder; this
/// picks it up (the shared HandoffWatch), checks it again (NowPlayingLookProposal) and either
/// saves it, when "Let Claude style songs directly" is on, or holds it as the suggestion Settings,
/// Desk, Now Playing shows with Save and Dismiss. Each step answers in
/// now-playing-looks-result.json: `pending_approval` at once, then `applied` or `rejected`.
///
/// Saved looks live in now-playing-looks.json (owner-only), the user's own approved data, until
/// Clear. The look for what plays is resolved once per song and kept in memory only, keyed by app,
/// title and artist; nothing here logs a title.
@MainActor
@Observable
final class NowPlayingLookStore {
    static let shared = NowPlayingLookStore()

    struct Paths {
        var support: URL

        static var standard: Paths { Paths(support: HandoffFiles.support) }

        var request: URL { support.appendingPathComponent(NowPlayingLookProposal.requestFile) }
        var result: URL { support.appendingPathComponent(NowPlayingLookProposal.resultFile) }
        var looks: URL { support.appendingPathComponent(NowPlayingLookProposal.looksFile) }
    }

    /// The suggestion waiting for the user, if any (memory only).
    private(set) var pending: NowPlayingLookProposal?
    /// The approved looks, oldest first.
    private(set) var saved: [ProposedSongLook] = []

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let direct: () -> Bool
    @ObservationIgnored private let styleOn: () -> Bool
    @ObservationIgnored private let now: () -> Date
    @ObservationIgnored private var approved: [String: SongLook] = [:]
    /// Resolved looks by NowPlayingLooks.cacheKey, in memory only.
    @ObservationIgnored private var cache: [String: SongLook] = [:]
    @ObservationIgnored private var watching = false
    @ObservationIgnored private var loaded = false

    /// The cache holds at most this many songs; it starts over past that.
    static let cacheLimit = 200

    init(paths: Paths = .standard,
         direct: @escaping () -> Bool = { UserDefaults.desk.bool(forKey: NowPlayingLooks.directKey) },
         styleOn: @escaping () -> Bool = { UserDefaults.desk.bool(forKey: NowPlayingLooks.styleKey) },
         now: @escaping () -> Date = Date.init) {
        self.paths = paths
        self.direct = direct
        self.styleOn = styleOn
        self.now = now
    }

    /// Reads the looks file, watches Sanduhr's folder for requests and takes one already waiting.
    func start(watch: HandoffWatch? = nil) {
        loadIfNeeded()
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    func loadIfNeeded() {
        guard !loaded else { return }
        loaded = true
        set(NowPlayingLookProposal.readLooks(try? Data(contentsOf: paths.looks)))
    }

    /// The look for what plays: its approved look, else the seeded one; resolved once per song.
    func look(for info: NowPlayingInfo) -> SongLook {
        _ = saved   // a view drawing the look redraws when the saved looks change
        let key = NowPlayingLooks.cacheKey(info)
        if let hit = cache[key] { return hit }
        if cache.count >= Self.cacheLimit { cache.removeAll() }
        let look = NowPlayingLooks.resolve(info, approved: approved)
        cache[key] = look
        return look
    }

    // MARK: - Requests

    func check() {
        guard let data = HandoffFiles.take(paths.request) else { return }
        receive(data)
    }

    func receive(_ data: Data) {
        switch NowPlayingLookProposal.decode(data, now: now()) {
        case .discard:
            return
        case .refused(let id, let reasons):
            writeResult(id: id, status: .rejected, reasons: reasons)
        case .proposal(let p):
            if direct() {
                save(p)
            } else {
                if let old = pending {
                    writeResult(id: old.id, status: .rejected, reasons: ["replaced by a newer suggestion"])
                }
                pending = p
                writeResult(id: p.id, status: .pendingApproval)
            }
        }
    }

    /// Save: the waiting looks join the user's.
    func approve() {
        guard let p = pending else { return }
        pending = nil
        save(p)
    }

    /// Dismiss: dropped, and the server's caller hears so.
    func dismiss() {
        guard let p = pending else { return }
        pending = nil
        writeResult(id: p.id, status: .rejected, reasons: ["dismissed by the user"])
    }

    /// Clear: every saved look goes, and the file with them; songs go back to their seeded looks.
    func clear() {
        try? FileManager.default.removeItem(at: paths.looks)
        set([])
    }

    private func save(_ p: NowPlayingLookProposal) {
        loadIfNeeded()
        let merged = NowPlayingLookProposal.merge(p.looks, into: saved)
        try? FileManager.default.createDirectory(at: paths.support, withIntermediateDirectories: true)
        HandoffFiles.writeOwnerOnly(NowPlayingLookProposal.looksJSON(merged), to: paths.looks)
        set(merged)
        writeResult(id: p.id, status: .applied, saved: p.looks.count)
    }

    private func set(_ looks: [ProposedSongLook]) {
        saved = looks
        approved = NowPlayingLookProposal.byKey(looks)
        cache.removeAll()
    }

    private func writeResult(id: String, status: NowPlayingLookProposal.Status, reasons: [String] = [], saved: Int? = nil) {
        let data = NowPlayingLookProposal.resultJSON(id: id, status: status, reasons: reasons, saved: saved,
                                                     styleOn: styleOn(), now: now())
        HandoffFiles.writeOwnerOnly(data, to: paths.result)
    }
}
