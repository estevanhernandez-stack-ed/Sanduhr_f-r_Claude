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
    case settings

    var id: String { rawValue }

    var title: String {
        switch self {
        case .claudeCode: "Claude Code folders"
        case .accounts: "Accounts and keys"
        case .records: "History and records"
        case .deskMessages: "Desk messages"
        case .themes: "Your themes"
        case .settings: "All settings"
        }
    }

    var detail: String {
        switch self {
        case .claudeCode: "Takes out what Sanduhr added to each folder, so its files are as they were before."
        case .accounts: "Remove Account for each account, its key and Keychain items included."
        case .records: "The vault, usage history, snapshots, seats.json and the rest of Sanduhr's folder in Application Support."
        case .deskMessages: "messages.txt, kept as messages.txt.previous beside it."
        case .themes: "The themes you added or Claude made for you."
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

    static let themesName = "themes"
    static let integrationsName = "integrations"
    static let previousSuffix = ".previous"

    var themesDir: URL { supportDir.appendingPathComponent(Self.themesName, isDirectory: true) }

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
            let n = supportEntries(env, keepThemes: true, keepIntegrations: false).count
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
        if parts.contains(.settings) {
            for d in env.domains {
                steps.append(ResetStep(part: .settings, kind: .preferences, target: d.name))
            }
        }
        return steps
    }

    /// The top-level items of Sanduhr's folder the records step deletes: everything but the
    /// themes (their own part) and, while a receipt is left, `integrations/` (the scripts those
    /// entries run and the receipts that remove them).
    static func supportEntries(_ env: ResetEnvironment, keepThemes: Bool, keepIntegrations: Bool) -> [URL] {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: env.supportDir.path) else { return [] }
        return names.sorted().compactMap { name in
            if keepThemes && name == ResetEnvironment.themesName { return nil }
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
            results.append(ResetResult(step: step, error: error))
        }
        return results
    }

    private static func run(_ step: ResetStep, in env: ResetEnvironment) throws {
        let fm = FileManager.default
        switch step.kind {
        case .claudeCodeEntry:
            guard let folder = step.folder, let kind = step.integration else { return }
            try env.installer.remove(kind, folder: folder)
            // Remove leaves an entry that isn't Sanduhr's any more alone and still drops the
            // receipt; anything else left is a failure.
            if env.installer.status(kind, folder: folder).isOurs { throw StepFailure("Sanduhr's entry is still there") }
        case .account:
            let r = env.accounts.remove(step.target)
            if !r.succeeded { throw StepFailure(keyFailure(r)) }
        case .legacyKeys:
            let r = env.accounts.signOut(nil)
            if !r.succeeded { throw StepFailure(keyFailure(r)) }
        case .supportFiles:
            // A receipt still here means a folder's entry is still in place: keep what removes it.
            let keepIntegrations = !env.installer.loadReceipts().isEmpty
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
        case .preferences:
            guard let d = env.domains.first(where: { $0.name == step.target }) else { return }
            d.defaults.removePersistentDomain(forName: d.name)
            if let left = d.defaults.persistentDomain(forName: d.name), !left.isEmpty {
                throw StepFailure("still has settings")
            }
        }
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

    /// Store and slot kind only, as Sign Out logs it: never a label or a value.
    private static func keyFailure(_ r: SignOutResult) -> String {
        let f = r.failures.map { "\(AccountRegistry.slotKind($0.account)) in the \($0.store.rawValue) store" }
        return "could not remove " + f.joined(separator: ", ")
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
