import Foundation
import Security

/// Credential storage for the Claude sessionKey (and optional cf_clearance).
///
/// **Strategy — the Keychain for signed builds, a 0600 file otherwise.**
/// - Signed with the team's Developer ID (team 82BSR56X5J, read from the running app's own
///   code signature): generic-password Keychain items, service `com.626labs.sanduhr`, account
///   `sessionKey` / `cf_clearance`, accessible after first unlock, no access-control object.
///   The default ACL trusts the app's designated requirement (bundle id + team), which a signed
///   update keeps, so updates never prompt.
/// - Ad-hoc or unsigned (dev builds, `SIGN_IDENTITY=-`): the file, as before.
/// - `SANDUHR_KEYCHAIN=1` in the environment forces the Keychain on any build, for testing that
///   path on a dev build. Each ad-hoc rebuild has a new signature, so macOS may then ask for the
///   login password to let the new build read the item. Never on by default.
///
/// **Moving the file over, once.** The first signed launch that finds the file copies each value
/// into the Keychain, reads it back and compares, and only then deletes the file. Any failure
/// leaves the file in place and the launch carries on with it; the next launch tries again
/// (CredentialMigration.swift). Values are never logged.
///
/// **History: why the file existed.** Through 2.3.1 every build used the file:
/// - Default Keychain ACLs bind items to the exact code signature that wrote them, so every
///   ad-hoc rebuild triggered a "Sanduhr wants to use the Keychain" login-password prompt. Real
///   friction while iterating. A Developer ID signature is stable, which is why signed builds
///   now use the Keychain.
/// - A biometric-ACL Keychain entry (`.userPresence`) fixes the reinstall issue in principle but
///   has edge cases on macOS when created from an accessory app with a fresh LAContext:
///   `SecItemAdd` can fail silently instead of prompting, leaving no entry (hit in testing on
///   macOS 15). So the Keychain items carry no access control, and no LAContext is involved.
/// - The Python reference implementation used plaintext JSON, and file permissions keep other
///   users on the machine out.
///
/// Type name kept as `KeychainStore` to avoid a big rename everywhere.
enum KeychainStore {

    // MARK: Public API (unchanged shape)

    /// A nil or empty value deletes the account. Goes to whichever store this launch uses.
    static func set(_ value: String?, account: String) {
        do {
            if let value, !value.isEmpty {
                try active.backend.set(value, account: account)
            } else {
                try active.backend.delete(account: account)
            }
        } catch {
            NSLog("Sanduhr: failed to save credential \(account) to the \(active.kind.rawValue) store: \(error)")
        }
    }

    static func get(account: String) -> String? {
        active.backend.get(account: account)
    }

    static func exists(account: String) -> Bool {
        active.backend.exists(account: account)
    }

    /// The store this launch uses (state.yaml's `credentials_store`).
    static var kind: CredentialStoreKind { active.kind }

    // MARK: Choosing the store

    private struct Active: Sendable {
        var kind: CredentialStoreKind
        var backend: any CredentialBackend
    }

    /// Decided once, on first use, with the one-time move from the file.
    private static let active: Active = {
        let file = FileBackend.standard
        let keychain = KeychainBackend(service: KeychainBackend.service)
        let env = ProcessInfo.processInfo.environment["SANDUHR_KEYCHAIN"]
        let isSigned = env == "1" || CodeSignature.teamIdentifier() == CodeSignature.team
        let r = CredentialMigration.resolve(isSigned: isSigned, file: file, keychain: keychain,
                                            accounts: KeychainAccount.all)
        switch r.outcome {
        case .migrated?: NSLog("Sanduhr: moved credentials from the file to the Keychain")
        case .keptFileBecause(let why)?: NSLog("Sanduhr: kept the credentials file: \(why)")
        case .nothingToMove?, nil: break
        }
        return r.store == .keychain ? Active(kind: .keychain, backend: keychain)
                                    : Active(kind: .file, backend: file)
    }()
}

enum KeychainAccount {
    static let sessionKey   = "sessionKey"
    static let cfClearance  = "cf_clearance"
    static let all = [sessionKey, cfClearance]
}

// MARK: - Code signature

enum CodeSignature {
    /// The team whose Developer ID signs release builds.
    static let team = "82BSR56X5J"

    /// The running app's team identifier, nil for ad-hoc and unsigned builds.
    static func teamIdentifier() -> String? {
        var code: SecCode?
        guard SecCodeCopySelf([], &code) == errSecSuccess, let code else { return nil }
        var staticCode: SecStaticCode?
        guard SecCodeCopyStaticCode(code, [], &staticCode) == errSecSuccess, let staticCode else { return nil }
        var info: CFDictionary?
        let flags = SecCSFlags(rawValue: kSecCSSigningInformation)
        guard SecCodeCopySigningInformation(staticCode, flags, &info) == errSecSuccess,
              let dict = info as? [String: Any] else { return nil }
        return dict[kSecCodeInfoTeamIdentifier as String] as? String
    }
}

// MARK: - File backend

/// `~/Library/Application Support/Sanduhr/credentials.json`, a JSON dict of
/// `{"sessionKey": "...", "cf_clearance": "..."}`, mode 0600. The file is removed once it holds
/// nothing, so a completed move to the Keychain leaves no file behind.
struct FileBackend: CredentialBackend {
    let url: URL

    static let standard: FileBackend = {
        let fm = FileManager.default
        let base = (try? fm.url(for: .applicationSupportDirectory,
                                in: .userDomainMask,
                                appropriateFor: nil, create: true))
            ?? fm.homeDirectoryForCurrentUser
                .appendingPathComponent("Library/Application Support")
        let dir = base.appendingPathComponent("Sanduhr", isDirectory: true)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true,
                                attributes: [.posixPermissions: 0o700])
        return FileBackend(url: dir.appendingPathComponent("credentials.json"))
    }()

    func get(account: String) -> String? {
        let v = load()[account]
        return (v?.isEmpty ?? true) ? nil : v
    }

    func set(_ value: String, account: String) throws {
        var creds = load()
        creds[account] = value
        try save(creds)
    }

    func delete(account: String) throws {
        var creds = load()
        guard creds.removeValue(forKey: account) != nil else { return }
        try save(creds)
    }

    private func load() -> [String: String] {
        guard let data = try? Data(contentsOf: url),
              let dict = try? JSONDecoder().decode([String: String].self, from: data)
        else { return [:] }
        return dict
    }

    private func save(_ dict: [String: String]) throws {
        let fm = FileManager.default
        if dict.isEmpty {
            if fm.fileExists(atPath: url.path) { try fm.removeItem(at: url) }
            return
        }
        let data = try JSONEncoder().encode(dict)
        try data.write(to: url, options: [.atomic])
        // Lock it down: owner read/write only, no group or other access.
        try fm.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
}

// MARK: - Keychain backend

/// Generic-password items in the login keychain. No access-control object (see the history
/// above); the default ACL trusts the app that wrote the item.
struct KeychainBackend: CredentialBackend {
    static let service = "com.626labs.sanduhr"
    let service: String

    struct Failure: Error, CustomStringConvertible {
        var operation: String
        var status: OSStatus
        var description: String { "Keychain \(operation) failed (OSStatus \(status))" }
    }

    private func query(_ account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: account]
    }

    func get(account: String) -> String? {
        var q = query(account)
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        guard SecItemCopyMatching(q as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data,
              let value = String(data: data, encoding: .utf8), !value.isEmpty
        else { return nil }
        return value
    }

    func set(_ value: String, account: String) throws {
        let data = Data(value.utf8)
        let update: [String: Any] = [kSecValueData as String: data,
                                     kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock]
        let status = SecItemUpdate(query(account) as CFDictionary, update as CFDictionary)
        if status == errSecSuccess { return }
        guard status == errSecItemNotFound else { throw Failure(operation: "update", status: status) }
        var add = query(account)
        add[kSecValueData as String] = data
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        add[kSecAttrLabel as String] = "Sanduhr für Claude (\(account))"
        let added = SecItemAdd(add as CFDictionary, nil)
        guard added == errSecSuccess else { throw Failure(operation: "add", status: added) }
    }

    func delete(account: String) throws {
        let status = SecItemDelete(query(account) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw Failure(operation: "delete", status: status)
        }
    }
}
