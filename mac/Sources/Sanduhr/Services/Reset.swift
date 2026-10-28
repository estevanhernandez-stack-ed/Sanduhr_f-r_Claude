import Foundation

/// Settings v2, slice 5: Reset Sanduhr… on About. What a reset removes, in the order it removes
/// it, and the run itself.
///
/// **The order is the point.** Claude Code folders come first: Sanduhr's entries there (the MCP
/// server, the statusline, the meters mod and its switch, the glow hooks) are undone through the
/// receipts in `integrations/installs.json`, and those receipts live in the Application Support
/// folder a later step deletes (F20). Then the accounts, each through Remove Account, so the
/// Keychain items go with the store's own rules (a dev build clears only its file, never the
/// release build's Keychain items). Then the files. The preferences domains go last, because the
/// account list the accounts step reads lives there.
///
/// **A failed step doesn't stop the rest.** Each step is tried on its own and reported; a Claude
/// Code folder whose file changed underneath (no longer JSON Sanduhr can edit, say) fails alone,
/// and its receipt stays, so `integrations/` stays with it and Settings, Claude Code can still
/// remove that entry after the next launch. With every step done, nothing of Sanduhr's is left.
///
/// Everything a step touches comes from `ResetEnvironment`: the real folders, Keychain and
/// defaults only in `ResetEnvironment.standard`, which only the sheet's confirmed button and the
/// read-only `reset --dry-run` hook build. Tests pass temp folders, a fake credential store and
/// throwaway defaults suites.
enum ResetPart: String, CaseIterable, Identifiable, Sendable {
    case claudeCode
    case accounts
    case records
    case deskMessages
    case themes
    case widgets
    case settings

    var id: String { rawValue }

    var title: String {
        switch self {
        case .claudeCode: "Claude Code folders"
        case .accounts: "Accounts and keys"
        case .records: "History and records"
        case .deskMessages: "Desk messages"
        case .themes: "Your themes"
        case .widgets: "Your widgets"
        case .settings: "All settings"
        }
    }

    var detail: String {
        switch self {
        case .claudeCode: "Takes out what Sanduhr added to each folder, so its files are as they were before."
        case .accounts: "Remove Account for each account: its key and cf_clearance, its history and its settings."
        case .records: "The vault, usage history, snapshots, seats.json and the rest of Sanduhr's folder in Application Support."
        case .deskMessages: "messages.txt, kept as messages.txt.previous beside it (an older messages.txt.previous is replaced)."
        case .themes: "The themes you added or Claude made for you."
        case .widgets: "The widgets you installed from the catalog, and the folder they live in."
        case .settings: "Both preferences domains, com.626labs.sanduhr and com.626labs.sanduhr.desk."
        }
    }

    /// state.yaml's name for the part.
    var key: String {
        switch self {
        case .claudeCode: "claude_code"
        case .accounts: "accounts"
        case .records: "records"
        case .deskMessages: "desk_messages"
        case .themes: "themes"
        case .widgets: "widgets"
        case .settings: "settings"
        }
    }
}

/// One preferences domain the settings step removes, with the defaults object that owns it.
struct ResetDomain {
    let name: String
    let defaults: UserDefaults
}

/// Where a reset looks and what it acts through.
struct ResetEnvironment {
    /// Undoes the Claude Code entries; its receipts say which folders hold one.
    var installer: IntegrationInstaller
    /// The accounts, over the credential stores Sign Out clears.
    var accounts: AccountRegistry
    /// `~/Library/Application Support/Sanduhr`.
    var supportDir: URL
    /// `~/Library/Application Support/Desk/messages.txt`.
    var messagesFile: URL
    var domains: [ResetDomain]

    /// Whether the accounts step clears the Keychain. A dev (ad-hoc) build clears only its own
    /// credentials file: the release build's Keychain items on the same Mac stay.
    var clearsKeychain: Bool { accounts.stores.contains { $0.kind == .keychain } }

    static let devBuildKeysNote = "This dev build clears its own key file; the release build's Keychain items stay."

    static let themesName = "themes"
    static let widgetsName = "widgets"
    static let integrationsName = "integrations"
    static let previousSuffix = ".previous"

    var themesDir: URL { supportDir.appendingPathComponent(Self.themesName, isDirectory: true) }
    var widgetsDir: URL { supportDir.appendingPathComponent(Self.widgetsName, isDirectory: true) }

    /// The real places. Built only by the sheet's confirmed button and the dry-run hook.
    static var standard: ResetEnvironment {
        ResetEnvironment(installer: .standard,
                         accounts: KeychainStore.accounts,
                         supportDir: HistoryStore.Files.standard.dir,
                         messagesFile: MessageEngine.fileURL,
                         domains: [ResetDomain(name: Reset.appDomain, defaults: .standard),
                                   ResetDomain(name: Reset.deskDomain, defaults: .desk)])
    }
}

/// One thing a reset does. `target` names it for the sheet (a folder, an account); it never
/// reaches a log or state.yaml.
struct ResetStep: Equatable, Identifiable {
    enum Kind: String, Equatable {
        /// One Sanduhr entry in one Claude Code folder, undone through its receipt.
        case claudeCodeEntry = "claude_code_entry"
        /// Remove Account for one account.
        case account
        /// The keys saved before accounts existed (`sessionKey`, `cf_clearance`).
        case legacyKeys = "legacy_keys"
        /// Sanduhr's Application Support folder, less what another part keeps.
        case supportFiles = "support_files"
        case deskMessages = "desk_messages"
        case themes
        case widgets
        /// One preferences domain.
        case preferences
    }

    var part: ResetPart
    var kind: Kind
    /// What the sheet shows: a folder and an entry, an account, a domain.
    var target: String
    /// How many things the step removes (entries in a folder, files in a folder).
    var count: Int = 1
    /// For a Claude Code entry: the folder and the integration.
    var folder: String?
    var integration: IntegrationKind?

    var id: String { "\(part.rawValue)|\(kind.rawValue)|\(target)|\(integration?.rawValue ?? "")" }
}

/// What one step did.
struct ResetResult: Equatable, Identifiable {
    var step: ResetStep
    /// Nil when the step did what it said.
    var error: String?
    /// Something worth knowing about a step that did what it said (a dev build's keys).
    var note: String?

    var ok: Bool { error == nil }
    var id: String { step.id }
}

enum Reset {
    static let appDomain = "com.626labs.sanduhr"
    static let deskDomain = "com.626labs.sanduhr.desk"

    // MARK: The plan

    /// The Claude Code folders holding a receipt, in the order Settings lists them, each with
    /// its entries. Read only.
    static func claudeCodeFolders(_ env: ResetEnvironment) -> [(folder: String, kinds: [IntegrationKind])] {
        var out: [(folder: String, kinds: [IntegrationKind])] = []
        for r in env.installer.loadReceipts() {
            if let i = out.firstIndex(where: { $0.folder == r.folder }) {
                if !out[i].kinds.contains(r.kind) { out[i].kinds.append(r.kind) }
            } else {
                out.append((r.folder, [r.kind]))
            }
        }
        return out
    }

    /// The checked steps, in the order they run: Claude Code first, then accounts and keys, then
    /// files, preferences last. `folders` limits the Claude Code part to those folders (nil:
    /// every folder holding a receipt). Reads only; touches nothing.
    static func plan(_ parts: Set<ResetPart>, folders: Set<String>? = nil, in env: ResetEnvironment) -> [ResetStep] {
        var steps: [ResetStep] = []
        let fm = FileManager.default
        if parts.contains(.claudeCode) {
            // Newest install first: an entry added into an object another entry had filled is
            // taken out before that one, so each object gets its old inside back.
            for r in env.installer.loadReceipts().reversed() where folders?.contains(r.folder) ?? true {
                steps.append(ResetStep(part: .claudeCode, kind: .claudeCodeEntry,
                                       target: "\(r.kind.title), \(r.folder)",
                                       folder: r.folder, integration: r.kind))
            }
        }
        if parts.contains(.accounts) {
            for label in env.accounts.labels {
                steps.append(ResetStep(part: .accounts, kind: .account, target: label))
            }
            if env.accounts.anythingToSignOut(nil) {
                steps.append(ResetStep(part: .accounts, kind: .legacyKeys, target: "Keys saved before accounts"))
            }
        }
        if parts.contains(.records) {
            let n = supportEntries(env, keepThemes: true, keepIntegrations: receiptsUnreadable(env)).count
            if n > 0 {
                steps.append(ResetStep(part: .records, kind: .supportFiles, target: env.supportDir.lastPathComponent, count: n))
            }
        }
        if parts.contains(.deskMessages), fm.fileExists(atPath: env.messagesFile.path) {
            steps.append(ResetStep(part: .deskMessages, kind: .deskMessages, target: env.messagesFile.lastPathComponent))
        }
        if parts.contains(.themes), fm.fileExists(atPath: env.themesDir.path) {
            let n = (try? fm.contentsOfDirectory(atPath: env.themesDir.path))?.count ?? 0
            steps.append(ResetStep(part: .themes, kind: .themes, target: ResetEnvironment.themesName, count: n))
        }
        if parts.contains(.widgets), fm.fileExists(atPath: env.widgetsDir.path) {
            let n = (try? fm.contentsOfDirectory(atPath: env.widgetsDir.path))?.count ?? 0
            steps.append(ResetStep(part: .widgets, kind: .widgets, target: ResetEnvironment.widgetsName, count: n))
        }
        if parts.contains(.settings) {
            for d in env.domains {
                steps.append(ResetStep(part: .settings, kind: .preferences, target: d.name))
            }
        }
        return steps
    }

    /// `integrations/installs.json` is there but doesn't read as a list of receipts (a newer
    /// format, or damaged): the entries it records can't be listed or undone here, so
    /// `integrations/` stays whatever else is checked, and the sheet says so.
    static func receiptsUnreadable(_ env: ResetEnvironment) -> Bool {
        guard let data = try? Data(contentsOf: env.installer.receiptsURL) else { return false }
        return (try? JSONDecoder().decode([IntegrationReceipt].self, from: data)) == nil
    }

    /// `integrations/` stays: a receipt is left (an unchecked or failed folder) or the receipts
    /// can't be read.
    static func keepsIntegrations(_ env: ResetEnvironment) -> Bool {
        !env.installer.loadReceipts().isEmpty || receiptsUnreadable(env)
    }

    /// The top-level items of Sanduhr's folder the records step deletes: everything but the
    /// themes and widgets (their own parts) and, while a receipt is left, `integrations/` (the scripts those
    /// entries run and the receipts that remove them).
    static func supportEntries(_ env: ResetEnvironment, keepThemes: Bool, keepIntegrations: Bool) -> [URL] {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: env.supportDir.path) else { return [] }
        return names.sorted().compactMap { name in
            if keepThemes && (name == ResetEnvironment.themesName || name == ResetEnvironment.widgetsName) { return nil }
            if keepIntegrations && name == ResetEnvironment.integrationsName { return nil }
            return env.supportDir.appendingPathComponent(name)
        }
    }

    // MARK: The dry run

    /// `reset --dry-run` (the smoke action): the plan for every part checked, as kinds and
    /// counts in run order. Never a path, a folder, a label or a domain's contents.
    struct Report: Equatable {
        struct Row: Equatable {
            var part: String
            var kind: String
            var count: Int
        }
        var rows: [Row]
        /// Distinct Claude Code folders the first step visits.
        var folders: Int
    }

    static func report(_ steps: [ResetStep]) -> Report {
        var rows: [Report.Row] = []
        for s in steps {
            if let last = rows.last, last.part == s.part.key, last.kind == s.kind.rawValue {
                rows[rows.count - 1].count += s.count
            } else {
                rows.append(Report.Row(part: s.part.key, kind: s.kind.rawValue, count: s.count))
            }
        }
        let folders = Set(steps.compactMap(\.folder)).count
        return Report(rows: rows, folders: folders)
    }

    static func dryRun(in env: ResetEnvironment) -> Report {
        report(plan(Set(ResetPart.allCases), in: env))
    }

    // MARK: The run

    /// Does `steps` in order, each on its own: a failure is recorded and the next step still
    /// runs. The only caller outside tests is the sheet's Reset and Quit, after the person
    /// confirmed.
    static func perform(_ steps: [ResetStep], in env: ResetEnvironment) -> [ResetResult] {
        var results: [ResetResult] = []
        for step in steps {
            var error: String?
            do {
                try run(step, in: env)
            } catch let e {
                error = describe(e)
            }
            var note: String?
            if step.part == .accounts, !env.clearsKeychain { note = ResetEnvironment.devBuildKeysNote }
            results.append(ResetResult(step: step, error: error, note: note))
        }
        return results
    }

    /// The last sweep, at quit after a reset: the checked file and preferences parts once more,
    /// for anything a stray write put back after the run. Safe to run any number of times.
    static func sweep(_ parts: Set<ResetPart>, in env: ResetEnvironment) {
        let fm = FileManager.default
        if parts.contains(.records) {
            for url in supportEntries(env, keepThemes: true, keepIntegrations: keepsIntegrations(env)) {
                try? fm.removeItem(at: url)
            }
        }
        if parts.contains(.themes) { try? fm.removeItem(at: env.themesDir) }
        if parts.contains(.widgets) { try? fm.removeItem(at: env.widgetsDir) }
        removeIfEmpty(env.supportDir)
        if parts.contains(.settings) {
            for d in env.domains { d.defaults.removePersistentDomain(forName: d.name) }
        }
    }

    private static func run(_ step: ResetStep, in env: ResetEnvironment) throws {
        let fm = FileManager.default
        switch step.kind {
        case .claudeCodeEntry:
            guard let folder = step.folder, let kind = step.integration else { return }
            let installer = env.installer
            let file = installer.configFile(kind, folder: folder)
            let held = installer.loadReceipts().filter { $0.file == file && $0.kind == kind }
            try installer.remove(kind, folder: folder)
            // Remove leaves an entry that isn't Sanduhr's any more alone and drops the receipt.
            // One of Sanduhr's still there is a failure, and its receipt goes back, so it can
            // still be removed later.
            if installer.status(kind, folder: folder).isOurs {
                installer.saveReceipts(installer.loadReceipts() + held)
                throw StepFailure("Sanduhr's entry is still there; kept to remove later")
            }
            dropBackup(file, folder: folder, in: env)
        case .account:
            let r = env.accounts.remove(step.target)
            if !r.succeeded { throw StepFailure(keyFailure(r)) }
        case .legacyKeys:
            let r = env.accounts.signOut(nil)
            if !r.succeeded { throw StepFailure(keyFailure(r)) }
        case .supportFiles:
            // A receipt still here means a folder's entry is still in place: keep what removes
            // it. Receipts that can't be read are kept the same.
            let keepIntegrations = keepsIntegrations(env)
            var failed = 0
            for url in supportEntries(env, keepThemes: true, keepIntegrations: keepIntegrations) {
                do { try fm.removeItem(at: url) } catch { failed += 1 }
            }
            removeIfEmpty(env.supportDir)
            if failed > 0 { throw StepFailure("\(failed) could not be deleted") }
        case .deskMessages:
            let previous = URL(fileURLWithPath: env.messagesFile.path + ResetEnvironment.previousSuffix)
            if fm.fileExists(atPath: previous.path) { try fm.removeItem(at: previous) }
            try fm.moveItem(at: env.messagesFile, to: previous)
        case .themes:
            try fm.removeItem(at: env.themesDir)
            removeIfEmpty(env.supportDir)
        case .widgets:
            try fm.removeItem(at: env.widgetsDir)
            removeIfEmpty(env.supportDir)
        case .preferences:
            guard let d = env.domains.first(where: { $0.name == step.target }) else { return }
            d.defaults.removePersistentDomain(forName: d.name)
            if let left = d.defaults.persistentDomain(forName: d.name), !left.isEmpty {
                throw StepFailure("still has settings")
            }
        }
    }

    /// Once no receipt names `file` and none of Sanduhr's entries is left in it, the copy
    /// Install kept beside it has nothing more to offer: it goes, even when the file changed
    /// since (an edit of someone else's between install and reset), so nothing of Sanduhr's stays
    /// in the folder.
    private static func dropBackup(_ file: String, folder: String, in env: ResetEnvironment) {
        let installer = env.installer
        guard !installer.loadReceipts().contains(where: { $0.file == file }) else { return }
        let kinds = IntegrationKind.allCases.filter { installer.configFile($0, folder: folder) == file }
        guard !kinds.contains(where: { installer.status($0, folder: folder).isOurs }) else { return }
        try? FileManager.default.removeItem(atPath: file + IntegrationInstaller.backupSuffix)
    }

    private static func removeIfEmpty(_ dir: URL) {
        let fm = FileManager.default
        if let left = try? fm.contentsOfDirectory(atPath: dir.path), left.isEmpty {
            try? fm.removeItem(at: dir)
        }
    }

    struct StepFailure: Error, Equatable {
        let message: String
        init(_ message: String) { self.message = message }
    }

    /// Store and slot kind only, as Sign Out logs it: never a label or a value. A key left in
    /// the Keychain is said so, since the account it belonged to is gone from the list.
    private static func keyFailure(_ r: SignOutResult) -> String {
        let f = r.failures.map { "\(AccountRegistry.slotKind($0.account)) in the \($0.store.rawValue) store" }
        var text = "could not remove " + f.joined(separator: ", ")
        if r.failures.contains(where: { $0.store == .keychain }) {
            text += "; it stays in the Keychain: delete the \(KeychainBackend.service) items in Keychain Access"
        }
        return text
    }

    /// What the result list says for a failure.
    static func describe(_ error: Error) -> String {
        switch error {
        case let f as StepFailure: return f.message
        case IntegrationInstaller.Failure.malformed:
            return "the file changed and isn't JSON Sanduhr can edit; left as it is"
        case IntegrationInstaller.Failure.writeFailed:
            return "the file couldn't be written, or kept changing"
        case IntegrationInstaller.Failure.scriptsMissing:
            return "Sanduhr's scripts are missing"
        default:
            return (error as NSError).localizedDescription
        }
    }
}
