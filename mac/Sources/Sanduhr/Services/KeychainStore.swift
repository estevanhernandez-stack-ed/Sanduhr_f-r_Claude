import Foundation
import LocalAuthentication

/// Credential storage for the Claude sessionKey (and optional cf_clearance).
///
/// **Current strategy — plaintext file, chmod 600.**
/// `~/Library/Application Support/Sanduhr/credentials.json`, JSON dict of
/// `{"sessionKey": "...", "cf_clearance": "..."}`, permissions restricted
/// to the owning user.
///
/// Why not Keychain (right now)?
/// - Default Keychain ACLs bind items to the exact code signature that
///   wrote them — any ad-hoc rebuild triggers a "Sanduhr wants to use the
///   Keychain" login-password prompt. Real friction for iterating builds.
/// - A biometric-ACL Keychain entry (`.userPresence`) fixes the reinstall
///   issue in principle but has edge cases on macOS when created from an
///   accessory app with a fresh LAContext — `SecItemAdd` can fail silently
///   instead of prompting, leaving zero entry (which is exactly what we
///   hit in testing on macOS 15).
/// - The Python reference implementation uses plaintext JSON at
///   `~/.claude-usage-widget/config.json`, so we're not losing ground.
/// - File permissions still keep other users on the machine out.
///
/// When the app is signed with a real Developer ID, swap back to Keychain:
/// - stable signature = no reinstall prompt
/// - no biometric-ACL complexity needed
/// Type name kept as `KeychainStore` to avoid a big rename everywhere.
enum KeychainStore {

    // MARK: Public API (unchanged shape)

    static func set(_ value: String?, account: String) {
        var creds = load()
        if let value = value, !value.isEmpty {
            creds[account] = value
        } else {
            creds.removeValue(forKey: account)
        }
        save(creds)
    }

    /// `context` kept for ABI compatibility with the earlier Touch-ID
    /// implementation; ignored here.
    static func get(account: String, context: LAContext? = nil) -> String? {
        _ = context
        let v = load()[account]
        return (v?.isEmpty ?? true) ? nil : v
    }

    static func exists(account: String) -> Bool {
        get(account: account) != nil
    }

    // MARK: Storage

    private static let fileURL: URL = {
        let fm = FileManager.default
        let base = (try? fm.url(for: .applicationSupportDirectory,
                                in: .userDomainMask,
                                appropriateFor: nil, create: true))
            ?? fm.homeDirectoryForCurrentUser
                .appendingPathComponent("Library/Application Support")
        let dir = base.appendingPathComponent("Sanduhr", isDirectory: true)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true,
                                attributes: [.posixPermissions: 0o700])
        return dir.appendingPathComponent("credentials.json")
    }()

    private static func load() -> [String: String] {
        guard let data = try? Data(contentsOf: fileURL),
              let dict = try? JSONDecoder().decode([String: String].self, from: data)
        else { return [:] }
        return dict
    }

    private static func save(_ dict: [String: String]) {
        let data = (try? JSONEncoder().encode(dict)) ?? Data("{}".utf8)
        do {
            try data.write(to: fileURL, options: [.atomic])
            // Lock it down: owner read/write only, no group or other access.
            try FileManager.default.setAttributes(
                [.posixPermissions: 0o600], ofItemAtPath: fileURL.path)
        } catch {
            NSLog("Sanduhr: failed to save credentials: \(error)")
        }
    }
}

enum KeychainAccount {
    static let sessionKey   = "sessionKey"
    static let cfClearance  = "cf_clearance"
}
