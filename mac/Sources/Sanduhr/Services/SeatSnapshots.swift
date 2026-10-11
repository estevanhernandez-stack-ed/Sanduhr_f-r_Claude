import Darwin
import Foundation

/// Per-seat meters (item 74): each Claude Code folder linked to a signed-in account gets that
/// account's numbers, so a session in a work seat and one in a personal seat each show their own
/// meters at the same time, whichever account Sanduhr shows.
///
/// Beside `snapshot.json` (still the active account's, for older scripts and the Desk) Sanduhr
/// writes `snapshot.<account_ref>.json`, the same schema, one per linked signed-in account, and
/// `seats.json`, `{"schema_version": 1, "seats": [{"folder": "<real path>", "account_ref": "…"}]}`.
/// Neither ever holds a label, an email or a key. The statusline, the meters mod and the MCP
/// server look their `CLAUDE_CONFIG_DIR` (unset: `~/.claude`) up in `seats.json`.
///
/// Off until two signed-in accounts have a folder linked: with one linked account or none, no file
/// here is written and any left from before is deleted, so an existing install reads exactly as it
/// did. Folder paths are written to `seats.json` only, never logged or put in state.yaml.
enum SeatSnapshots {
    static let schemaVersion = 1
    static let seatsName = "seats.json"
    /// Per-seat files start with the second linked signed-in account.
    static let minimumLinked = 2
    /// Seconds between the active fetch and each inactive linked account's fetch.
    static let stagger: TimeInterval = 5

    /// One linked folder and the account it shows.
    struct Seat: Equatable, Sendable {
        let folder: String
        let accountRef: String
    }

    /// Where the files live: Sanduhr's Application Support folder, or a test's temp folder.
    struct Files: Sendable {
        let dir: URL

        static var standard: Files { Files(dir: SnapshotWriter.url.deletingLastPathComponent()) }

        var seats: URL { dir.appendingPathComponent(SeatSnapshots.seatsName) }

        func snapshot(_ ref: String) -> URL { dir.appendingPathComponent("snapshot.\(ref).json") }

        /// The per-account snapshot files on disk, by account ref.
        func snapshotRefs() -> Set<String> {
            let names = (try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []
            return Set(names.compactMap(SeatSnapshots.ref(inFileName:)))
        }
    }

    // MARK: Which accounts

    /// The signed-in accounts with a folder linked, label to folder.
    static func linked(choices: [String: AccountDataChoices], signedIn: Set<String>) -> [String: String] {
        var out: [String: String] = [:]
        for (label, c) in choices where signedIn.contains(label) {
            if let folder = c.folder, !folder.isEmpty { out[label] = folder }
        }
        return out
    }

    /// Whether per-seat files are written at all.
    static func isOn(_ linked: [String: String]) -> Bool { linked.count >= minimumLinked }

    /// The accounts whose seats get their own meters: every linked one while on, none otherwise.
    static func seatAccounts(_ linked: [String: String]) -> Set<String> {
        isOn(linked) ? Set(linked.keys) : []
    }

    /// The seats `seats.json` lists, sorted by folder: none while off.
    static func seats(_ linked: [String: String], realPath: (String) -> String = realPath) -> [Seat] {
        guard isOn(linked) else { return [] }
        return linked.compactMap { label, folder in
            AccountRef.of(label).map { Seat(folder: realPath(folder), accountRef: $0) }
        }.sorted { ($0.folder, $0.accountRef) < ($1.folder, $1.accountRef) }
    }

    /// The fetch rule: the inactive linked accounts to fetch after the active one this cycle,
    /// only those following saw in use (`AccountFollow.othersInUse`), only while on. An account
    /// with no folder is never fetched for a seat.
    static func due(linked: [String: String], active: String?, inUse: Set<String>) -> [String] {
        guard isOn(linked) else { return [] }
        return linked.keys.filter { $0 != active && inUse.contains($0) }.sorted()
    }

    /// Settings, Claude Code, a folder box's Account row: whose meters a session in the folder
    /// shows. "Meters here show Work", or the active account's while the folder is no seat.
    static func metersHere(owner: String?, seatAccounts: Set<String>) -> String {
        guard let owner, seatAccounts.contains(owner) else { return "Meters here show the active account" }
        return "Meters here show \(owner)"
    }

    // MARK: Files

    /// A folder as seats.json writes it: `~` expanded, every symbolic link followed (realpath(3),
    /// as Python's `os.path.realpath` gives it), no trailing slash. A folder that is gone keeps its
    /// standardized spelling.
    static func realPath(_ path: String) -> String {
        let expanded = (path as NSString).expandingTildeInPath
        var resolved = AccountData.normalized(expanded)
        if let c = Darwin.realpath(expanded, nil) {
            resolved = String(cString: c)
            free(c)
        }
        while resolved.count > 1 && resolved.hasSuffix("/") { resolved.removeLast() }
        return resolved
    }

    /// The account ref in `snapshot.<ref>.json`, nil for any other name (snapshot.json included).
    static func ref(inFileName name: String) -> String? {
        let prefix = "snapshot.", suffix = ".json"
        guard name.hasPrefix(prefix), name.hasSuffix(suffix),
              name.count == prefix.count + 8 + suffix.count else { return nil }
        let ref = String(name.dropFirst(prefix.count).dropLast(suffix.count))
        return isRef(ref) ? ref : nil
    }

    /// Eight lowercase hex digits, as `AccountRef` writes them.
    static func isRef(_ s: String) -> Bool {
        s.count == 8 && s.allSatisfy { ("0"..."9").contains($0) || ("a"..."f").contains($0) }
    }

    static func encode(_ seats: [Seat]) -> Data {
        let rows: [[String: Any]] = seats.map { ["folder": $0.folder, "account_ref": $0.accountRef] }
        let root: [String: Any] = ["schema_version": schemaVersion, "seats": rows]
        let data = (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys, .withoutEscapingSlashes])) ?? Data()
        return data + Data("\n".utf8)
    }

    /// Brings the folder in line with `seats`: `seats.json` rewritten when its bytes change (or
    /// deleted when there are none), and every `snapshot.<ref>.json` whose account is no longer a
    /// seat deleted (signed out, removed, unlinked, renamed, or per-seat turned off).
    static func sync(_ seats: [Seat], in files: Files) {
        let keep = Set(seats.map(\.accountRef))
        for ref in files.snapshotRefs().subtracting(keep) {
            remove(files.snapshot(ref))
        }
        if seats.isEmpty {
            remove(files.seats)
        } else {
            write(encode(seats), to: files.seats)
        }
    }

    /// state.yaml's `snapshots` and `seats`: counts only, never a path or a ref.
    static func counts(in files: Files) -> (snapshots: Int, seats: Int) {
        let snapshots = files.snapshotRefs().count
        guard let data = try? Data(contentsOf: files.seats),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let rows = obj["seats"] as? [Any] else { return (snapshots, 0) }
        return (snapshots, rows.count)
    }

    /// Written whole, a temporary sibling renamed over the file, so a reader never sees half.
    private static func write(_ data: Data, to url: URL) {
        if let current = try? Data(contentsOf: url), current == data { return }
        let fm = FileManager.default
        let tmp = url.deletingLastPathComponent().appendingPathComponent(".\(seatsName).\(UUID().uuidString).tmp")
        do {
            try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            guard fm.createFile(atPath: tmp.path, contents: data, attributes: [.posixPermissions: 0o600]) else {
                throw CocoaError(.fileWriteUnknown)
            }
            guard rename(tmp.path, url.path) == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        } catch {
            unlink(tmp.path)
            NSLog("Sanduhr seats write failed (\(type(of: error)))")
        }
    }

    private static func remove(_ url: URL) {
        do {
            try FileManager.default.removeItem(at: url)
        } catch CocoaError.fileNoSuchFile {
            // Nothing to delete.
        } catch {
            NSLog("Sanduhr seat file delete failed (\(type(of: error)))")
        }
    }
}
