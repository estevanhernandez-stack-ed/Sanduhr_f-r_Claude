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
    /// The folder whose session proposed it, by name (`.claude-personal`); nil from older servers.
    var folder: String?
    /// `file` when the user opened it from a `.sanduhrpack` (item 76, slice B); nil from Claude.
    var origin: String?

    static let fileOrigin = "file"
    var fromFile: Bool { origin == Self.fileOrigin }

    var isPending: Bool { status == .pendingReview }
}

/// A request to turn a kept mod on in one folder, from `propose_install` (slice G2).
struct ModInstallRecord: Codable, Equatable, Identifiable, Sendable {
    enum Status: String, Codable, Sendable {
        case pendingReview = "pending_review"
        case approved, dismissed, replaced
    }

    var id: String
    var name: String
    /// The Claude Code folder's path, and its name as the server gives it.
    var folder: String
    var folderName: String
    var requestedAt: Date
    var status: Status
    var decidedAt: Date?

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
        var installRecords: URL { kept.dir.appendingPathComponent("install-requests.json") }
        var catalog: URL { support.appendingPathComponent(ModProposal.catalogFile) }
        var installRequest: URL { support.appendingPathComponent(ModProposal.installRequestFile) }
        var installResult: URL { support.appendingPathComponent(ModProposal.installResultFile) }
        var configRequest: URL { support.appendingPathComponent(ConfigProposal.requestFile) }
        var configResult: URL { support.appendingPathComponent(ConfigProposal.resultFile) }
        var configRecords: URL { kept.dir.appendingPathComponent("config-proposals.json") }
        var proposedConfig: URL { kept.dir.appendingPathComponent(".proposed-config", isDirectory: true) }
        func stagedConfig(_ id: String) -> URL { proposedConfig.appendingPathComponent(id + ".json") }
        func staged(_ id: String) -> URL { proposed.appendingPathComponent(id, isDirectory: true) }
    }

    private(set) var records: [ModProposalRecord] = []
    private(set) var installs: [ModInstallRecord] = []
    /// `propose_config`'s proposals (item 76, slice K).
    private(set) var configs: [ConfigProposalRecord] = []
    var allowed: Bool {
        didSet {
            guard allowed != oldValue else { return }
            defaults.set(allowed, forKey: Self.switchKey)
            writeSwitch()
            publishCatalog()
        }
    }

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let now: () -> Date
    @ObservationIgnored private var watching = false
    /// The Claude Code folders the catalog names, and the installer reading their settings.
    @ObservationIgnored private let folders: () -> [String]
    @ObservationIgnored private let installer: IntegrationInstaller
    /// A proposal arrived and waits.
    @ObservationIgnored var onProposal: ((ModProposalRecord) -> Void)?

    var pending: [ModProposalRecord] { records.filter(\.isPending) }
    /// Waiting proposals Claude sent, and those opened from a pack.
    var pendingFromClaude: [ModProposalRecord] { pending.filter { !$0.fromFile } }
    var pendingFromFiles: [ModProposalRecord] { pending.filter(\.fromFile) }
    var pendingInstalls: [ModInstallRecord] { installs.filter(\.isPending) }
    var pendingConfigs: [ConfigProposalRecord] { configs.filter(\.isPending) }

    init(paths: Paths = .standard, defaults: UserDefaults = .standard, now: @escaping () -> Date = Date.init,
         folders: @escaping () -> [String] = {
             ClaudeCodeFolders.discover(home: NSHomeDirectory(), environment: ProcessInfo.processInfo.environment).map(\.path)
         },
         installer: IntegrationInstaller = .standard) {
        self.paths = paths
        self.defaults = defaults
        self.now = now
        self.folders = folders
        self.installer = installer
        allowed = defaults.bool(forKey: Self.switchKey)
        records = Self.load(paths.records)
        installs = Self.load(paths.installRecords)
        configs = Self.load(paths.configRecords)
    }

    /// Writes the switch for the server and watches for requests.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        writeSwitch()
        publishCatalog()
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    func check() {
        if let data = HandoffFiles.take(paths.request, maxBytes: ModProposal.maxRequestBytes) { receive(data) }
        if let data = HandoffFiles.take(paths.installRequest, maxBytes: 4096) { receiveInstall(data) }
        if let data = HandoffFiles.take(paths.configRequest, maxBytes: ModProposal.maxRequestBytes) { receiveConfig(data) }
    }

    // MARK: Config proposals (item 76, slice K)

    func receiveConfig(_ data: Data) {
        switch ConfigProposal.decode(data, now: now()) {
        case .discard:
            return
        case .refused(let id, let reasons):
            answerConfig(id, status: "rejected", reason: "invalid_config", reasons: reasons, remedy: "Fix what reasons names and call again.")
        case .proposal(let p):
            guard allowed else {
                answerConfig(p.id, status: "rejected", reason: "switched_off",
                             remedy: "The user hasn't let agents propose mods. Tell them it is in Sanduhr, Mods & Config, New Mod, From Claude: Let agents propose mods.")
                return
            }
            guard let folder = namedFolders().first(where: { $0.name == p.folder }) else {
                answerConfig(p.id, status: "rejected", reason: "unknown_folder", remedy: "Sanduhr knows no Claude Code folder named '\(p.folder)'.")
                return
            }
            do {
                try FileManager.default.createDirectory(at: paths.proposedConfig, withIntermediateDirectories: true)
                HandoffFiles.writeOwnerOnly(p.json, to: paths.stagedConfig(p.id))
            }
            catch {
                answerConfig(p.id, status: "error", reason: "stage_failed", remedy: "Sanduhr couldn't save the proposal for review.")
                return
            }
            configs.append(ConfigProposalRecord(id: p.id, summary: p.summary, folderName: p.folder, folder: AccountData.normalized(folder.path),
                                                files: p.files.count, settings: p.settings.count, servers: p.servers.count,
                                                requestedAt: p.requestedAt, status: .pendingReview))
            save()
            answerConfig(p.id, status: "pending_review", folder: p.folder,
                         remedy: "The user reviews it in Sanduhr, Mods & Config, By Folder. Call get_mod_status with request_id for the decision.")
        }
    }

    /// The staged proposal, read again and checked again, as a pack for the Open sheet.
    func configPack(_ r: ConfigProposalRecord) -> (pack: ModPackReader.Unpacked, sha256: String)? {
        guard let data = FileManager.default.contents(atPath: paths.stagedConfig(r.id).path),
              case .proposal(let p) = ConfigProposal.decode(data, now: r.requestedAt) else { return nil }
        return (p.pack, ConfigImport.sha(data))
    }

    /// The user imported some of it (approved) or dismissed it.
    func decideConfig(_ id: String, approved: Bool) {
        guard let i = configs.firstIndex(where: { $0.id == id && $0.isPending }) else { return }
        configs[i].status = approved ? .approved : .dismissed
        configs[i].decidedAt = now()
        try? FileManager.default.removeItem(at: paths.stagedConfig(id))
        save()
    }

    private func answerConfig(_ id: String, status: String, folder: String? = nil, reason: String? = nil,
                              reasons: [String] = [], remedy: String? = nil) {
        HandoffFiles.writeOwnerOnly(ConfigProposal.resultJSON(id: id, status: status, folder: folder, reason: reason,
                                                              reasons: reasons, remedy: remedy, now: now()),
                                    to: paths.configResult)
    }

    // MARK: The catalog and install requests (slice G2)

    /// The folders by the name the server uses (`.claude-personal`), first path for a name.
    private func namedFolders() -> [(name: String, path: String)] {
        var out: [(String, String)] = []
        for path in folders() {
            let name = (path as NSString).lastPathComponent
            if ModProposal.isFolderName(name), !out.contains(where: { $0.0 == name }) { out.append((name, path)) }
        }
        return out
    }

    /// The newest kept copy of each name.
    private func newestKept() -> [KeptMod] {
        var seen = Set<String>()
        return paths.kept.list().filter { seen.insert($0.name).inserted }
    }

    /// mods-catalog.json for `list_catalog`: the kept mods and where each is on, by folder name.
    /// Gone while the switch is off.
    func publishCatalog() {
        guard allowed else {
            try? FileManager.default.removeItem(at: paths.catalog)
            return
        }
        let named = namedFolders()
        let mods: [[String: Any]] = newestKept().map { mod in
            var where_: [String: String] = [:]
            for f in named {
                switch paths.kept.state(mod, folder: f.path, installer: installer) {
                case .on: where_[f.name] = "on"
                case .off: where_[f.name] = "off"
                case .otherVersion: where_[f.name] = "other_version"
                }
            }
            var o: [String: Any] = ["name": mod.name, "origin": mod.proposed == true ? "proposed" : "session", "folders": where_]
            if let v = mod.version { o["version"] = v }
            if !mod.description.isEmpty { o["description"] = mod.description }
            return o
        }
        let doc: [String: Any] = ["schema_version": 1, "folders": named.map(\.name), "mods": mods]
        if let data = try? JSONSerialization.data(withJSONObject: doc, options: [.sortedKeys]) {
            HandoffFiles.writeOwnerOnly(data, to: paths.catalog)
        }
    }

    func receiveInstall(_ data: Data) {
        switch ModProposal.decodeInstall(data, now: now()) {
        case .discard:
            return
        case .refused(let id, let why):
            answerInstall(id, status: "rejected", reason: "invalid_params", remedy: why)
        case .request(let id, let name, let folderName, let at):
            guard allowed else {
                answerInstall(id, status: "rejected", reason: "switched_off",
                              remedy: "The user hasn't let agents propose mods. Tell them it is in Sanduhr, Mods & Config, New Mod, From Claude: Let agents propose mods.")
                return
            }
            guard let mod = newestKept().first(where: { $0.name == name }) else {
                answerInstall(id, status: "rejected", reason: "not_kept", remedy: "The user keeps no mod named '\(name)'.")
                return
            }
            guard let folder = namedFolders().first(where: { $0.name == folderName }) else {
                answerInstall(id, status: "rejected", reason: "unknown_folder", remedy: "Sanduhr knows no Claude Code folder named '\(folderName)'.")
                return
            }
            if paths.kept.state(mod, folder: folder.path, installer: installer) == .on {
                answerInstall(id, status: "rejected", reason: "already_on", remedy: "\(name) is on in \(folderName) already.")
                return
            }
            for i in installs.indices where installs[i].isPending && installs[i].name == name && installs[i].folderName == folderName {
                installs[i].status = .replaced
                installs[i].decidedAt = now()
            }
            installs.append(ModInstallRecord(id: id, name: name, folder: AccountData.normalized(folder.path), folderName: folderName,
                                             requestedAt: at, status: .pendingReview))
            save()
            answerInstall(id, status: "pending_review", name: name, folder: folderName,
                          remedy: "The user decides in Sanduhr, Mods & Config, New Mod, From Claude. Call get_mod_status with request_id.")
        }
    }

    /// The user turned the mod on (approved) or dismissed the request.
    func decideInstall(_ id: String, approved: Bool) {
        guard let i = installs.firstIndex(where: { $0.id == id && $0.isPending }) else { return }
        installs[i].status = approved ? .approved : .dismissed
        installs[i].decidedAt = now()
        save()
    }

    private func answerInstall(_ id: String, status: String, name: String? = nil, folder: String? = nil,
                               reason: String? = nil, remedy: String? = nil) {
        let data = HandoffFiles.resultJSON(id: id, status: status,
                                           fields: [("name", name), ("folder", folder), ("reason", reason), ("remedy", remedy)],
                                           now: now())
        HandoffFiles.writeOwnerOnly(data, to: paths.installResult)
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
                                           files: p.files.count, bytes: p.bytes, requestedAt: p.requestedAt, status: .pendingReview,
                                           folder: p.folder)
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

    /// Whether a waiting proposal's staged copy is still there to try: its manifest is on disk.
    func isStaged(_ r: ModProposalRecord) -> Bool {
        r.isPending && FileManager.default.fileExists(atPath: paths.staged(r.id).appendingPathComponent(".claude-plugin/plugin.json").path)
    }

    /// The Claude Code folder whose session proposed it, when Sanduhr knows a folder by that name.
    func folderPath(_ r: ModProposalRecord) -> String? {
        guard let name = r.folder else { return nil }
        return namedFolders().first { $0.name == name }?.path
    }

    /// Try in a Session on a waiting proposal (slice G3): Claude Code loads the staged copy for one
    /// session, with the folder whose session proposed it (Claude Code's default folder when unknown). It stays a proposal: nothing is kept, and neither
    /// its status nor the server's index changes.
    /// `fallback` is the folder used when the proposal names none Sanduhr knows (a pack's).
    @discardableResult
    func trySession(_ id: String, claude: String, in workingFolder: URL? = nil, fallback: String? = nil,
                    open: Bool = true) throws -> (start: URL, script: URL) {
        guard let r = records.first(where: { $0.id == id && $0.isPending }), isStaged(r) else { throw KeptMods.Failure.gone }
        return try KeptMods.trySession(name: r.name, dir: paths.staged(id).path, config: folderPath(r) ?? fallback, claude: claude,
                                       in: workingFolder, open: open)
    }

    /// Approve: the staged files become a kept mod.
    @discardableResult
    func approve(_ id: String, fallback: String? = nil) throws -> KeptMod {
        guard let i = records.firstIndex(where: { $0.id == id && $0.isPending }) else { throw KeptMods.Failure.writeFailed }
        let kept = try paths.kept.keep(paths.staged(id).path, session: records[i].fromFile ? "pack" : "claude",
                                       folder: folderPath(records[i]) ?? fallback, proposed: true, now: now())
        records[i].status = .approved
        records[i].decidedAt = now()
        records[i].keptStamp = kept.stamp
        removeStage(id)
        save()
        publishCatalog()
        return kept
    }

    /// What opening a pack did.
    enum Opened: Equatable {
        case staged(ModProposalRecord)
        /// The same files are kept already.
        case alreadyKept(KeptMod)
    }

    /// Stages a mod opened from a pack (item 76, slice B) to be reviewed like a proposal: files,
    /// Check, Try, Keep or Dismiss. The agents switch has no say (the user opened the file), the
    /// server never hears of it, and a waiting proposal of the same name is replaced.
    func open(_ pack: ModPackReader.Opened) throws -> Opened {
        let p = pack.proposal
        if let stamp = pack.stamp, let kept = paths.kept.list().first(where: { $0.name == p.name && $0.stamp == stamp }) {
            return .alreadyKept(kept)
        }
        do { try stage(p) } catch { throw ModPackReader.Refusal("Sanduhr couldn't save the files for review.") }
        for i in records.indices where records[i].isPending && records[i].name == p.name {
            records[i].status = .replaced
            records[i].decidedAt = now()
            removeStage(records[i].id)
        }
        let record = ModProposalRecord(id: p.id, name: p.name, version: p.version, summary: p.summary,
                                       files: p.files.count, bytes: p.bytes, requestedAt: now(), status: .pendingReview,
                                       origin: ModProposalRecord.fileOrigin)
        records.append(record)
        save()
        return .staged(record)
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
        let decidedInstalls = installs.filter { !$0.isPending }.sorted { ($0.decidedAt ?? .distantPast) > ($1.decidedAt ?? .distantPast) }
        let keepInstalls = Set(decidedInstalls.prefix(Self.keepDecided).map(\.id))
        installs = installs.filter { $0.isPending || keepInstalls.contains($0.id) }
        let decidedConfigs = configs.filter { !$0.isPending }.sorted { ($0.decidedAt ?? .distantPast) > ($1.decidedAt ?? .distantPast) }
        let keepConfigs = Set(decidedConfigs.prefix(Self.keepDecided).map(\.id))
        configs = configs.filter { $0.isPending || keepConfigs.contains($0.id) }
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? encoder.encode(records) { HandoffFiles.writeOwnerOnly(data, to: paths.records) }
        if let data = try? encoder.encode(installs) { HandoffFiles.writeOwnerOnly(data, to: paths.installRecords) }
        if let data = try? encoder.encode(configs) { HandoffFiles.writeOwnerOnly(data, to: paths.configRecords) }
        // A pack the user opened is theirs: the server's index lists Claude's proposals only.
        var index: [[String: Any]] = records.filter { !$0.fromFile }.map { r in
            var o: [String: Any] = ["id": r.id, "kind": "mod", "name": r.name, "status": r.status.rawValue]
            if let v = r.version { o["version"] = v }
            if let d = r.decidedAt { o["decided_at"] = HandoffFiles.stamp(d) }
            return o
        }
        index += installs.map { r in
            var o: [String: Any] = ["id": r.id, "kind": "install", "name": r.name, "folder": r.folderName, "status": r.status.rawValue]
            if let d = r.decidedAt { o["decided_at"] = HandoffFiles.stamp(d) }
            return o
        }
        index += configs.map { r in
            var o: [String: Any] = ["id": r.id, "kind": "config", "name": r.folderName, "folder": r.folderName, "status": r.status.rawValue]
            if let d = r.decidedAt { o["decided_at"] = HandoffFiles.stamp(d) }
            return o
        }
        if let data = try? JSONSerialization.data(withJSONObject: ["schema_version": 1, "proposals": index], options: [.sortedKeys]) {
            HandoffFiles.writeOwnerOnly(data, to: paths.status)
        }
    }

    private static func load<T: Decodable>(_ url: URL) -> [T] {
        guard let data = FileManager.default.contents(atPath: url.path) else { return [] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return (try? decoder.decode([T].self, from: data)) ?? []
    }
}
