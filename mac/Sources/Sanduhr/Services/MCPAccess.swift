import Foundation

/// One account as `mcp-access.json` names it (item 47): what `sanduhr-mcp` may read for it.
/// Only accounts that share something have an entry.
struct MCPAccessEntry: Equatable, Sendable {
    /// The hashed label (`AccountRef`), never the label.
    let accountRef: String
    /// The registry's active account, whose meters snapshot.json holds.
    let active: Bool
    /// `.meters` or `.activity`; `.off` never gets an entry.
    let share: ShareChoice
    /// `history.<label>.json`, the meter history file the server may open. The file name carries
    /// the label because that is the file's name on disk; the server opens it and never returns
    /// the name.
    let historyFile: String
    /// The account's project-names choice, with activity only: Hidden makes the server return
    /// the record's short code for every project, never a name or a path.
    let names: ProjectNamesChoice?
    /// The linked folder's vault id (`VaultFolderID`), with activity and Keep a record only.
    let vaultID: String?
    /// The linked Claude Code folder, absolute, with activity and Live only or Keep a record
    /// only. The one path in the file: the server must open the folder's session logs for the
    /// rolling 1, 7 and 30-day windows of `get_local_burn_by_project` and `get_model_usage`
    /// (Windows reads the raw logs there too; the vault holds whole local days only).
    let liveFolder: String?
}

/// `~/Library/Application Support/Sanduhr/mcp-access.json` (item 47): how the MCP server learns
/// each account's Share with your agents choice without reading the app's defaults, the Keychain or
/// any label. The app rewrites it whenever choices, links, accounts or the active account
/// change; the server reads it on every call and shares nothing without it.
///
///     { "schema_version": 1,
///       "accounts": [ { "account_ref": "1a2b3c4d", "active": true, "share": "activity",
///                       "history_file": "history.<label>.json", "names": "hidden",
///                       "vault_id": "0123456789abcdef", "live_folder": "/Users/x/.claude" } ] }
///
/// Keys appear only when allowed: `history_file` with any sharing, `names` and `live_folder`
/// with activity and a linked folder that is read (Live only or Keep a record), `vault_id`
/// with activity and Keep a record. Accounts with sharing off are left out entirely.
enum MCPAccess {
    static let schemaVersion = 1
    static let fileName = "mcp-access.json"

    static var standardURL: URL {
        HistoryStore.Files.standard.dir.appendingPathComponent(fileName)
    }

    /// The entries for the accounts in list order. Pure: the choices come in, nothing is read.
    static func entries(labels: [String], active: String?,
                        choices: [String: AccountDataChoices]) -> [MCPAccessEntry] {
        labels.compactMap { label in
            let c = choices[label] ?? .defaults
            guard c.share != .off, let ref = AccountRef.of(label) else { return nil }
            let activity = c.share == .activity
            let folder = c.folder.flatMap { $0.isEmpty ? nil : AccountData.normalized($0) }
            let reads = activity && folder != nil && c.activity != .off
            return MCPAccessEntry(
                accountRef: ref,
                active: label == active,
                share: c.share,
                historyFile: "history.\(label).json",
                names: activity ? c.names : nil,
                vaultID: reads && c.activity == .record ? folder.map(VaultFolderID.of) : nil,
                liveFolder: reads ? folder : nil)
        }
    }

    /// The file's bytes: sorted keys and no escaped slashes, so the same choices always give
    /// the same bytes and an unchanged file is never rewritten.
    static func encode(_ entries: [MCPAccessEntry]) -> Data {
        var accounts: [[String: Any]] = []
        for e in entries {
            var o: [String: Any] = [
                "account_ref": e.accountRef,
                "active": e.active,
                "share": e.share.rawValue,
                "history_file": e.historyFile,
            ]
            if let names = e.names { o["names"] = names.rawValue }
            if let id = e.vaultID { o["vault_id"] = id }
            if let folder = e.liveFolder { o["live_folder"] = folder }
            accounts.append(o)
        }
        let root: [String: Any] = ["schema_version": schemaVersion, "accounts": accounts]
        let data = try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys, .withoutEscapingSlashes])
        return data ?? Data()
    }

    enum WriteOutcome: Equatable {
        case written, unchanged, failed
    }

    /// Writes `data` to `url` unless the file already holds it: a temporary sibling, owner-only
    /// (it names a folder and a history file), renamed over the file so a reader never sees half
    /// of it. Failures are logged by error type only.
    @discardableResult
    static func write(_ data: Data, to url: URL = standardURL) -> WriteOutcome {
        if let current = try? Data(contentsOf: url), current == data { return .unchanged }
        let fm = FileManager.default
        let tmp = url.deletingLastPathComponent()
            .appendingPathComponent(".\(fileName).\(UUID().uuidString).tmp")
        do {
            try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            guard fm.createFile(atPath: tmp.path, contents: data, attributes: [.posixPermissions: 0o600]) else {
                throw CocoaError(.fileWriteUnknown)
            }
            guard rename(tmp.path, url.path) == 0 else {
                throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
            }
            return .written
        } catch {
            unlink(tmp.path)
            NSLog("Sanduhr mcp-access write failed (\(type(of: error)))")
            return .failed
        }
    }

    /// Builds and writes the file for the current accounts.
    @discardableResult
    static func sync(labels: [String], active: String?, choices: [String: AccountDataChoices],
                     to url: URL = standardURL) -> WriteOutcome {
        write(encode(entries(labels: labels, active: active, choices: choices)), to: url)
    }
}
