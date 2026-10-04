import Foundation

/// An account's secrets as the active store holds them.
struct AccountCredentials: Equatable, Sendable {
    var sessionKey: String?
    var cfClearance: String?
}

/// Why an account change was refused. Never carries a label or a value: failures name the slot
/// kind only (`sessionKey`, `cf_clearance`), so an error is safe to log.
enum AccountError: Error, Equatable, CustomStringConvertible {
    case invalidLabel
    case duplicateLabel
    case unknownLabel
    case missingSessionKey
    case writeFailed(slot: String)
    case readBackMismatch(slot: String)

    var description: String {
        switch self {
        case .invalidLabel: "label must be 1 to 32 letters, digits, spaces, underscores or hyphens"
        case .duplicateLabel: "an account with that label already exists"
        case .unknownLabel: "no account with that label"
        case .missingSessionKey: "a session key is required"
        case .writeFailed(let slot): "could not save \(slot)"
        case .readBackMismatch(let slot): "\(slot) did not read back as written"
        }
    }
}

/// What the legacy promotion did at launch (spec "Upgrade", step 1).
enum LegacyPromotion: Equatable, Sendable {
    case promoted
    case nothingToPromote
    /// The legacy slots stay and this launch runs on them; the next launch tries again. The
    /// failure names the slot kind only.
    case keptLegacyBecause(CredentialMigrationFailure)
}

/// Named Claude accounts (item 36), ported from Windows' `AccountStore`. Each account is a label
/// plus its own session key and optional cf_clearance, under the Windows slot names
/// `sessionKey:{label}` and `cf_clearance:{label}` in the active credential store.
///
/// **The registry lives in the app's defaults** (`accounts`, the ordered labels, and
/// `activeAccount`), not in the Keychain as on Windows: reading it needs no Keychain access, so
/// menus and the Accounts page never prompt. Labels are not secrets, but they never go into a
/// log, snapshot.json or state.yaml.
///
/// **Legacy slots.** Through 2.3.x the single key lived under `sessionKey` / `cf_clearance`.
/// `promoteLegacy()` moves it to Personal at launch; until that succeeds there is no active
/// label and every slot falls back to the legacy name, so the launch runs on the old key.
///
/// The backends and the defaults are injected: the real Keychain/file and UserDefaults in the
/// app, in-memory fakes under test.
final class AccountRegistry: @unchecked Sendable {
    static let listKey = "accounts"
    static let activeKey = "activeAccount"
    /// The first account, and the one the legacy key becomes.
    static let defaultLabel = "Personal"
    static let maxLabelLength = 32

    /// Where secrets are read and written this launch.
    let backend: any CredentialBackend
    /// Every store Sign Out clears (both, whichever one this launch uses).
    let stores: [(kind: CredentialStoreKind, backend: any CredentialBackend)]
    /// Also holds the sign-in marker (SignInGate), which follows renames and removals.
    let defaults: DefaultsStore
    /// The per-account history files, which follow renames and removals. Nil leaves files alone.
    let history: HistoryStore.Files?

    init(backend: any CredentialBackend,
         stores: [(kind: CredentialStoreKind, backend: any CredentialBackend)],
         defaults: DefaultsStore, history: HistoryStore.Files? = nil) {
        self.backend = backend
        self.stores = stores
        self.defaults = defaults
        self.history = history
    }

    // MARK: Labels and slots

    /// The Windows rule, `^[A-Za-z0-9 _-]{1,32}$`, checked by character so nothing like a
    /// trailing newline slips past a regex anchor.
    static func isValid(_ label: String) -> Bool {
        let scalars = label.unicodeScalars
        guard (1...maxLabelLength).contains(scalars.count) else { return false }
        return scalars.allSatisfy { s in
            switch s {
            case "A"..."Z", "a"..."z", "0"..."9", " ", "_", "-": true
            default: false
            }
        }
    }

    /// The slot for `kind` (KeychainAccount.sessionKey or .cfClearance) of `label`, or the
    /// legacy unlabelled slot when there is no label.
    static func slot(_ kind: String, for label: String?) -> String {
        label.map { "\(kind):\($0)" } ?? kind
    }

    static func slots(for label: String?) -> [String] {
        KeychainAccount.all.map { slot($0, for: label) }
    }

    /// The kind part of a slot name, the only part a log may show.
    static func slotKind(_ slot: String) -> String {
        slot.split(separator: ":", maxSplits: 1).first.map(String.init) ?? slot
    }

    /// The saved labels, read straight from `defaults` (the launch's file-to-Keychain move needs
    /// them before a registry exists).
    static func savedLabels(in defaults: DefaultsStore) -> [String] {
        (defaults.object(forKey: listKey) as? [String] ?? []).filter(isValid)
    }

    // MARK: Registry

    /// The accounts in list order. Never reads a credential.
    var labels: [String] { Self.savedLabels(in: defaults) }

    /// The active account, nil only when there are no accounts. A saved label that is no longer
    /// listed reads as the first account. Never reads a credential.
    var active: String? {
        let list = labels
        if let saved = defaults.object(forKey: Self.activeKey) as? String, list.contains(saved) {
            return saved
        }
        return list.first
    }

    func setActive(_ label: String) throws {
        guard labels.contains(label) else { throw AccountError.unknownLabel }
        defaults.set(label, forKey: Self.activeKey)
    }

    /// Whether `label` would clash with an account other than `except`. Case-insensitive: the
    /// history files are named by label, and the Mac's file system ignores case.
    private func clashes(_ label: String, except: String? = nil) -> Bool {
        labels.contains { $0 != except && $0.lowercased() == label.lowercased() }
    }

    private func writeList(_ list: [String]) {
        defaults.set(list, forKey: Self.listKey)
    }

    /// Adds an account with its key, at the end of the list. The first account becomes active.
    func add(_ label: String, sessionKey: String, cfClearance: String? = nil) throws {
        guard Self.isValid(label) else { throw AccountError.invalidLabel }
        guard !clashes(label) else { throw AccountError.duplicateLabel }
        guard !sessionKey.isEmpty else { throw AccountError.missingSessionKey }
        var values = [(Self.slot(KeychainAccount.sessionKey, for: label), sessionKey)]
        if let cfClearance, !cfClearance.isEmpty {
            values.append((Self.slot(KeychainAccount.cfClearance, for: label), cfClearance))
        }
        try write(values)
        let wasEmpty = labels.isEmpty
        writeList(labels + [label])
        if wasEmpty { defaults.set(label, forKey: Self.activeKey) }
    }

    /// Writes a listed account's secrets: a value replaces, nil keeps, an empty string deletes.
    func save(_ label: String, sessionKey: String? = nil, cfClearance: String? = nil) throws {
        guard labels.contains(label) else { throw AccountError.unknownLabel }
        for (kind, value) in [(KeychainAccount.sessionKey, sessionKey), (KeychainAccount.cfClearance, cfClearance)] {
            guard let value else { continue }
            let slot = Self.slot(kind, for: label)
            do {
                if value.isEmpty { try backend.delete(account: slot) } else { try backend.set(value, account: slot) }
            } catch {
                throw AccountError.writeFailed(slot: kind)
            }
        }
    }

    /// Whether this launch's store holds a session key for `label`: signed in, as the Accounts
    /// page and the menus show it. Asks for attributes only, so it never prompts.
    func hasKey(_ label: String) -> Bool {
        backend.holds(account: Self.slot(KeychainAccount.sessionKey, for: label))
    }

    func credentials(for label: String) -> AccountCredentials {
        AccountCredentials(sessionKey: backend.get(account: Self.slot(KeychainAccount.sessionKey, for: label)),
                           cfClearance: backend.get(account: Self.slot(KeychainAccount.cfClearance, for: label)))
    }

    /// Renames an account in place. Its secrets move the 2.3.2 way: written under the new
    /// label, read back, and only then is the list changed and the old slots deleted. Its history
    /// file and sign-in marker follow. A failed write or read-back leaves everything as it was.
    func rename(_ old: String, to new: String) throws {
        guard Self.isValid(new) else { throw AccountError.invalidLabel }
        var list = labels
        guard let index = list.firstIndex(of: old) else { throw AccountError.unknownLabel }
        guard old != new else { return }
        guard !clashes(new, except: old) else { throw AccountError.duplicateLabel }
        let creds = credentials(for: old)
        var values: [(String, String)] = []
        if let key = creds.sessionKey { values.append((Self.slot(KeychainAccount.sessionKey, for: new), key)) }
        if let cf = creds.cfClearance { values.append((Self.slot(KeychainAccount.cfClearance, for: new), cf)) }
        try write(values)
        let wasActive = active == old
        list[index] = new
        writeList(list)
        if wasActive { defaults.set(new, forKey: Self.activeKey) }
        // The new copy is verified; a stale old slot left by a failed delete holds nothing the
        // list points at.
        for slot in Self.slots(for: old) { try? backend.delete(account: slot) }
        history?.rename(old, to: new)
        SignInGate.rename(old, to: new, in: defaults)
    }

    /// Writes each value and reads it back. On a failure the slots written so far are deleted
    /// again and the error names the slot kind.
    private func write(_ values: [(String, String)]) throws {
        var written: [String] = []
        func undo() { for slot in written { try? backend.delete(account: slot) } }
        for (slot, value) in values {
            do { try backend.set(value, account: slot) } catch {
                undo()
                throw AccountError.writeFailed(slot: Self.slotKind(slot))
            }
            written.append(slot)
            guard backend.get(account: slot) == value else {
                undo()
                throw AccountError.readBackMismatch(slot: Self.slotKind(slot))
            }
        }
    }

    // MARK: Sign Out and Remove

    /// Sign Out for one account (item 32, per account): deletes its key and cf_clearance from
    /// both stores and forgets its sign-in marker. The account stays listed, signed out, with its
    /// history. The legacy unlabelled slots are swept
    /// too, so no pre-accounts copy is left behind in either store. Nil signs out the legacy
    /// slots alone (a launch still running on them).
    @discardableResult
    func signOut(_ label: String?) -> SignOutResult {
        var slots = Self.slots(for: nil)
        if let label { slots = Self.slots(for: label) + slots }
        SignInGate.forget(label, in: defaults)
        return SignOut.run(backends: stores, accounts: slots)
    }

    /// Whether either store still holds a secret of `label` (or a legacy one). Never reads a
    /// Keychain value, so it doesn't prompt on a dev build.
    func anythingToSignOut(_ label: String?) -> Bool {
        var slots = Self.slots(for: nil)
        if let label { slots = Self.slots(for: label) + slots }
        return SignOut.anythingStored(backends: stores, accounts: slots)
    }

    /// Remove Account: Sign Out, delete its history file, then drop it from the list. Removing the active account makes
    /// the next one active (the one after it, wrapping), or none when it was the last.
    @discardableResult
    func remove(_ label: String) -> SignOutResult {
        var list = labels
        guard let index = list.firstIndex(of: label) else { return SignOutResult(outcomes: []) }
        let wasActive = active == label
        let result = signOut(label)
        history?.delete(label)
        list.remove(at: index)
        writeList(list)
        if wasActive {
            defaults.set(list.isEmpty ? nil : list[index % list.count], forKey: Self.activeKey)
        }
        return result
    }

    /// The next account after the active one, wrapping (Windows `CycleAccount`); nil with fewer
    /// than two.
    var nextLabel: String? {
        let list = labels
        guard list.count > 1, let current = active, let i = list.firstIndex(of: current) else { return nil }
        return list[(i + 1) % list.count]
    }

    // MARK: Upgrade

    /// The legacy key becomes Personal (spec "Upgrade", step 1), after the 2.3.2 file-to-Keychain
    /// move. Runs when the active store holds a legacy `sessionKey` and Personal has no key here:
    /// with no accounts yet (the upgrade), or with Personal listed but its key missing from this
    /// store (a dev build on the file beside a release build's registry). Writes Personal's
    /// slots, reads them back, registers Personal, and only then deletes the legacy slots. Any
    /// failed write keeps the legacy slots untouched (the 2.3.2 rule: never lose the key).
    func promoteLegacy() -> LegacyPromotion {
        let label = Self.defaultLabel
        let list = labels
        let legacyKey = KeychainAccount.sessionKey
        let legacyCF = KeychainAccount.cfClearance
        let keySlot = Self.slot(legacyKey, for: label)
        let cfSlot = Self.slot(legacyCF, for: label)
        guard list.isEmpty || (list.contains(label) && !backend.exists(account: keySlot)),
              let key = backend.get(account: legacyKey) else { return .nothingToPromote }
        var values = [(keySlot, key)]
        let cf = backend.get(account: legacyCF)
        if let cf { values.append((cfSlot, cf)) }
        do { try write(values) } catch {
            if case AccountError.readBackMismatch(let slot) = error {
                return .keptLegacyBecause(.readBackMismatch(account: slot))
            }
            if case AccountError.writeFailed(let slot) = error {
                return .keptLegacyBecause(.writeFailed(account: slot))
            }
            return .keptLegacyBecause(.writeFailed(account: legacyKey))
        }
        if cf == nil {
            // The legacy key had no cf_clearance, so a stale one under Personal must not pair with it.
            do { try backend.delete(account: cfSlot) } catch {
                try? backend.delete(account: keySlot)
                return .keptLegacyBecause(.clearFailed(account: legacyCF))
            }
        }
        if list.isEmpty {
            writeList([label])
            defaults.set(label, forKey: Self.activeKey)
        }
        for slot in Self.slots(for: nil) { try? backend.delete(account: slot) }
        return .promoted
    }

    /// The upgrade's one-time move of `history.json` to Personal's file (spec "Upgrade", step 2),
    /// once Personal is listed; never over an existing file. Safe to call at every launch.
    func adoptLegacyHistory() {
        guard labels.contains(Self.defaultLabel) else { return }
        history?.adoptLegacy(into: Self.defaultLabel)
    }

    /// Deletes the legacy slots from the active store: a key saved with no accounts creates
    /// Personal, which replaces whatever a launch was still running on.
    func dropLegacy() {
        for slot in Self.slots(for: nil) { try? backend.delete(account: slot) }
    }
}
