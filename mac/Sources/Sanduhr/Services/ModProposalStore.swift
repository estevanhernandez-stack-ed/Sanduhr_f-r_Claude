import Foundation
import Observation

/// One proposal as Sanduhr keeps it while the user decides, and after (item 75, slice G).
struct ModProposalRecord: Codable, Equatable, Identifiable, Sendable {
    enum Status: String, Codable, Sendable {
        case pendingReview = "pending_review"
        case approved, dismissed, replaced
    }

    var id: String
    var name: String
    var version: String?
    var summary: String
    var files: Int
    var bytes: Int
    var requestedAt: Date
    var status: Status
    var decidedAt: Date?
    /// The kept copy Approve made.
    var keptStamp: String?

    var isPending: Bool { status == .pendingReview }
}

/// Claude's proposed mods (item 75, slice G), the app's half of `propose_mod`. Only while
/// **Let agents propose mods** is on (off by default; `mods-agents.json` tells the server), a
/// request is checked again (ModProposal), its files staged read-only under
/// `mods/.proposed/<id>/` and held for review: the server hears `pending_review` at once. Approve
/// keeps the files as a kept mod (KeptMods), which turns on in no folder until the user does that
/// separately; Dismiss deletes them. Each decision goes into `mod-proposals.json`, names and
/// statuses only, for `get_mod_status`. A newer proposal under the same name replaces one still
/// waiting. Proposals survive a quit; nothing here runs a mod's code.
@MainActor
@Observable
final class ModProposalStore {
    static let shared = ModProposalStore()

    /// Mods & Config, New Mod, From Claude: "Let agents propose mods", off by default.
    nonisolated static let switchKey = "modsAgentsPropose"
    static let keepDecided = 30

    struct Paths {
        /// ~/Library/Application Support/Sanduhr: the switch, request, result and status files.
        var support: URL
        var kept: KeptMods

        static var standard: Paths { Paths(support: HandoffFiles.support, kept: .standard) }

        var request: URL { support.appendingPathComponent(ModProposal.requestFile) }
        var result: URL { support.appendingPathComponent(ModProposal.resultFile) }
        var status: URL { support.appendingPathComponent(ModProposal.statusFile) }
        var switchFile: URL { support.appendingPathComponent(ModProposal.switchFile) }
        var proposed: URL { kept.dir.appendingPathComponent(".proposed", isDirectory: true) }
        var records: URL { kept.dir.appendingPathComponent("proposals.json") }
        func staged(_ id: String) -> URL { proposed.appendingPathComponent(id, isDirectory: true) }
    }

    private(set) var records: [ModProposalRecord] = []
    var allowed: Bool {
        didSet {
            guard allowed != oldValue else { return }
            defaults.set(allowed, forKey: Self.switchKey)
            writeSwitch()
        }
    }

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let now: () -> Date
    @ObservationIgnored private var watching = false
    /// A proposal arrived and waits.
    @ObservationIgnored var onProposal: ((ModProposalRecord) -> Void)?

    var pending: [ModProposalRecord] { records.filter(\.isPending) }

    init(paths: Paths = .standard, defaults: UserDefaults = .standard, now: @escaping () -> Date = Date.init) {
        self.paths = paths
        self.defaults = defaults
        self.now = now
        allowed = defaults.bool(forKey: Self.switchKey)
        records = Self.load(paths.records)
    }

    /// Writes the switch for the server and watches for requests.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        writeSwitch()
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    func check() {
        guard let data = HandoffFiles.take(paths.request, maxBytes: ModProposal.maxRequestBytes) else { return }
        receive(data)
    }

    func receive(_ data: Data) {
        switch ModProposal.decode(data, now: now()) {
        case .discard:
            return
        case .refused(let id, let reasons):
            answer(id, status: "rejected", reason: "invalid_mod", reasons: reasons, remedy: "Fix what reasons names and call again.")
        case .proposal(let p):
            guard allowed else {
                answer(p.id, status: "rejected", reason: "switched_off",
                       remedy: "The user hasn't let agents propose mods. Tell them it is in Sanduhr, Mods & Config, New Mod, From Claude: Let agents propose mods.")
                return
            }
            do { try stage(p) } catch {
                answer(p.id, status: "error", reason: "stage_failed", remedy: "Sanduhr couldn't save the files for review.")
                return
            }
            for i in records.indices where records[i].isPending && records[i].name == p.name {
                records[i].status = .replaced
                records[i].decidedAt = now()
                removeStage(records[i].id)
            }
            let record = ModProposalRecord(id: p.id, name: p.name, version: p.version, summary: p.summary,
                                           files: p.files.count, bytes: p.bytes, requestedAt: p.requestedAt, status: .pendingReview)
            records.append(record)
            save()
            answer(p.id, status: "pending_review", name: p.name, version: p.version,
                   remedy: "The user reviews it in Sanduhr, Mods & Config, New Mod, From Claude. Call get_mod_status with request_id for the decision.")
            onProposal?(record)
        }
    }

    /// The staged folder Review reads and Check validates.
    func stagedPath(_ r: ModProposalRecord) -> String { paths.staged(r.id).path }

    /// The staged files, relative and sorted, with their text.
    func files(_ r: ModProposalRecord) -> [ModProposal.File] {
        let dir = paths.staged(r.id)
        guard let names = try? KeptMods.files(in: dir) else { return [] }
        return names.map { ModProposal.File(path: $0, text: (try? String(contentsOf: dir.appendingPathComponent($0), encoding: .utf8)) ?? "") }
    }

    /// Approve: the staged files become a kept mod.
    @discardableResult
    func approve(_ id: String) throws -> KeptMod {
        guard let i = records.firstIndex(where: { $0.id == id && $0.isPending }) else { throw KeptMods.Failure.writeFailed }
        let kept = try paths.kept.keep(paths.staged(id).path, session: "claude", proposed: true, now: now())
        records[i].status = .approved
        records[i].decidedAt = now()
        records[i].keptStamp = kept.stamp
        removeStage(id)
        save()
        return kept
    }

    func dismiss(_ id: String) {
        guard let i = records.firstIndex(where: { $0.id == id && $0.isPending }) else { return }
        records[i].status = .dismissed
        records[i].decidedAt = now()
        removeStage(id)
        save()
    }

    // MARK: Files

    private func stage(_ p: ModProposal) throws {
        let fm = FileManager.default
        let dir = paths.staged(p.id)
        if fm.fileExists(atPath: dir.path) { removeStage(p.id) }
        for f in p.files {
            let url = dir.appendingPathComponent(f.path)
            try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try Data(f.text.utf8).write(to: url)
            try fm.setAttributes([.posixPermissions: 0o444], ofItemAtPath: url.path)
        }
    }

    private func removeStage(_ id: String) {
        let dir = paths.staged(id)
        let fm = FileManager.default
        if let walk = fm.enumerator(atPath: dir.path) {
            while let rel = walk.nextObject() as? String {
                guard (walk.fileAttributes?[.type] as? FileAttributeType) == .typeRegular else { continue }
                try? fm.setAttributes([.posixPermissions: 0o644], ofItemAtPath: dir.appendingPathComponent(rel).path)
            }
        }
        try? fm.removeItem(at: dir)
    }

    private func answer(_ id: String, status: String, name: String? = nil, version: String? = nil,
                        reason: String? = nil, reasons: [String] = [], remedy: String? = nil) {
        HandoffFiles.writeOwnerOnly(ModProposal.resultJSON(id: id, status: status, name: name, version: version,
                                                           reason: reason, reasons: reasons, remedy: remedy, now: now()),
                                    to: paths.result)
    }

    private func writeSwitch() {
        let doc: [String: Any] = ["schema_version": 1, "propose": allowed]
        guard let data = try? JSONSerialization.data(withJSONObject: doc, options: [.sortedKeys]) else { return }
        HandoffFiles.writeOwnerOnly(data, to: paths.switchFile)
    }

    /// Keeps every waiting proposal and the last decided ones; writes the server's index.
    private func save() {
        let decided = records.filter { !$0.isPending }.sorted { ($0.decidedAt ?? .distantPast) > ($1.decidedAt ?? .distantPast) }
        let keep = Set(decided.prefix(Self.keepDecided).map(\.id))
        records = records.filter { $0.isPending || keep.contains($0.id) }
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? encoder.encode(records) { HandoffFiles.writeOwnerOnly(data, to: paths.records) }
        let index: [[String: Any]] = records.map { r in
            var o: [String: Any] = ["id": r.id, "name": r.name, "status": r.status.rawValue]
            if let v = r.version { o["version"] = v }
            if let d = r.decidedAt { o["decided_at"] = HandoffFiles.stamp(d) }
            return o
        }
        if let data = try? JSONSerialization.data(withJSONObject: ["schema_version": 1, "proposals": index], options: [.sortedKeys]) {
            HandoffFiles.writeOwnerOnly(data, to: paths.status)
        }
    }

    private static func load(_ url: URL) -> [ModProposalRecord] {
        guard let data = FileManager.default.contents(atPath: url.path) else { return [] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return (try? decoder.decode([ModProposalRecord].self, from: data)) ?? []
    }
}
