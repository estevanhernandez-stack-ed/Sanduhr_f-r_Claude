import Darwin
import Foundation

/// Per-seat meters (item 74): each Claude Code folder linked to an account gets that account's
/// numbers, so a session in a work seat and one in a personal seat each show their own meters at
/// the same time, whichever account Sanduhr shows.
///
/// Beside `snapshot.json` (still the active account's, for older scripts and the Desk) Sanduhr
/// writes `snapshot.<account_ref>.json`, the same schema, one per linked account, and `seats.json`,
/// `{"schema_version": 1, "seats": [{"folder": "<real path>", "account_ref": "…", "label": "…"}]}`.
/// `label` is the account's name as the user wrote it, for that folder's statusline to show; no
/// file here holds an email or a key, and a snapshot holds no label. The statusline, the meters
/// mod and the MCP server look their `CLAUDE_CONFIG_DIR` (unset: `~/.claude`) up in `seats.json`;
/// only the statusline reads `label`.
///
/// On while any account has a folder linked, signed in or not: a linked folder never shows another
/// account's numbers. A signed-out linked account keeps its row and gets a signed-out file (no
/// tiers), so its sessions say "sign in again". With no folder linked, no file here exists (any
/// left from before is deleted), so an install without links reads exactly as it did. Folder paths
/// are written to `seats.json` only, never logged or put in state.yaml. `SeatMeters` keeps them.
enum SeatSnapshots {
    static let schemaVersion = 1
    static let seatsName = "seats.json"
    /// Seconds between the active fetch and each inactive linked account's fetch.
    static let stagger: TimeInterval = 5

    /// One linked folder and the account it shows.
    struct Seat: Equatable, Sendable {
        let folder: String
        let accountRef: String
        /// The account's name, shown by that folder's statusline.
        var label = ""
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

    /// The listed accounts with a folder linked, label to folder, signed in or not.
    static func linked(choices: [String: AccountDataChoices], labels: [String]) -> [String: String] {
        var out: [String: String] = [:]
        for label in labels {
            if let folder = choices[label]?.folder, !folder.isEmpty { out[label] = folder }
        }
        return out
    }

    /// The seats `seats.json` lists, sorted by folder: one per linked account.
    static func seats(_ linked: [String: String], realPath: (String) -> String = realPath) -> [Seat] {
        linked.compactMap { label, folder in
            AccountRef.of(label).map { Seat(folder: realPath(folder), accountRef: $0, label: label) }
        }.sorted { ($0.folder, $0.accountRef) < ($1.folder, $1.accountRef) }
    }

    /// The fetch rule: the inactive signed-in linked accounts following saw in use
    /// (`AccountFollow.othersInUse`). An account with no folder is never fetched for a seat.
    static func due(linked: [String: String], signedIn: Set<String>, active: String?, inUse: Set<String>) -> [String] {
        linked.keys.filter { fetchable($0, linked: linked, signedIn: signedIn, active: active) && inUse.contains($0) }.sorted()
    }

    /// Whether an answer for `label` may still be written to its seat file: linked, signed in and
    /// not the active account (whose own fetch writes it).
    static func fetchable(_ label: String, linked: [String: String], signedIn: Set<String>, active: String?) -> Bool {
        linked[label] != nil && signedIn.contains(label) && label != active
    }

    /// Settings, Claude Code, a folder box's Account row: whose meters a session in the folder
    /// shows. "Meters here show Work", or the active account's for a folder no account links.
    static func metersHere(owner: String?) -> String {
        guard let owner else { return "Meters here show the active account" }
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
        let rows: [[String: Any]] = seats.map {
            var row: [String: Any] = ["folder": $0.folder, "account_ref": $0.accountRef]
            if !$0.label.isEmpty { row["label"] = $0.label }
            return row
        }
        let root: [String: Any] = ["schema_version": schemaVersion, "seats": rows]
        let data = (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys, .withoutEscapingSlashes])) ?? Data()
        return data + Data("\n".utf8)
    }

    /// Brings the folder in line with `seats`: `seats.json` rewritten when its bytes change (or
    /// deleted when there are none); every `snapshot.<ref>.json` whose account is no longer a seat
    /// deleted (removed, unlinked); a signed-out seat's file written signed out (no tiers), and a
    /// signed-out file left by an account signed in again deleted, until its next reading.
    static func sync(_ seats: [Seat], signedOut: Set<String> = [], in files: Files, now: Date = Date()) {
        let keep = Set(seats.map(\.accountRef))
        for ref in files.snapshotRefs().subtracting(keep) {
            remove(files.snapshot(ref))
        }
        for ref in keep {
            let url = files.snapshot(ref)
            if signedOut.contains(ref) {
                if !isSignedOut(url) { SnapshotWriter.writeSignedOut(accountRef: ref, now: now, to: url) }
            } else if isSignedOut(url) {
                remove(url)
            }
        }
        if seats.isEmpty {
            remove(files.seats)
        } else {
            write(encode(seats), to: files.seats)
        }
    }

    /// A renamed account's seat file follows it: moved to the new ref, `account_ref` rewritten.
    static func rename(_ oldRef: String, to newRef: String, in files: Files) {
        let from = files.snapshot(oldRef), to = files.snapshot(newRef)
        guard oldRef != newRef, !ResetGate.isOn, var obj = json(from) else { return }
        obj["account_ref"] = newRef
        guard let data = try? JSONSerialization.data(withJSONObject: obj, options: [.sortedKeys]) else { return }
        do {
            try data.write(to: to, options: .atomic)
            remove(from)
        } catch {
            NSLog("Sanduhr seat file rename failed (\(type(of: error)))")
        }
    }

    /// When a snapshot file's reading was taken, nil when it is missing or unreadable.
    static func capturedAt(_ url: URL) -> Date? {
        guard let s = json(url)?["captured_at"] as? String else { return nil }
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = iso.date(from: s) { return d }
        iso.formatOptions = [.withInternetDateTime]
        return iso.date(from: s)
    }

    /// The file `writeSignedOut` writes: an auth error with no tiers.
    static func isSignedOut(_ url: URL) -> Bool {
        guard let obj = json(url) else { return false }
        return obj["status"] as? String == "error" && obj["error_kind"] as? String == "session_expired"
            && (obj["tiers"] as? [Any])?.isEmpty == true
    }

    /// state.yaml's `snapshots` and `seats`: counts only, never a path or a ref.
    static func counts(in files: Files) -> (snapshots: Int, seats: Int) {
        let snapshots = files.snapshotRefs().count
        guard let rows = json(files.seats)?["seats"] as? [Any] else { return (snapshots, 0) }
        return (snapshots, rows.count)
    }

    private static func json(_ url: URL) -> [String: Any]? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }

    /// Written whole, a temporary sibling renamed over the file, so a reader never sees half;
    /// owner-only, put back to 0600 even when the bytes are unchanged.
    static func write(_ data: Data, to url: URL) {
        // Reset Sanduhr… ran: seats.json and the seat files stay gone.
        guard !ResetGate.isOn else { return }
        let fm = FileManager.default
        if let current = try? Data(contentsOf: url), current == data {
            try? fm.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
            return
        }
        let tmp = url.deletingLastPathComponent().appendingPathComponent(".\(seatsName).\(UUID().uuidString).tmp")
        do {
            try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            guard fm.createFile(atPath: tmp.path, contents: data, attributes: [.posixPermissions: 0o600]) else {
                throw CocoaError(.fileWriteUnknown)
            }
            guard Darwin.rename(tmp.path, url.path) == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
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
