import CryptoKit
import Foundation
import os

/// Where a vault message goes: fixed phrases and error type names only, never a path, a label,
/// a project, a uuid or a file's content (PRIVACY.md). Nil drops them.
typealias VaultLogSink = @Sendable (String) -> Void

enum VaultLog {
    private static let logger = Logger(subsystem: "com.626labs.sanduhr", category: "vault")

    /// The unified log. Every message passed here is a fixed phrase, so it is public.
    static let system: VaultLogSink = { message in
        logger.notice("vault \(message, privacy: .public)")
    }

    /// "<operation> failed (<ErrorType>)": the type only, never its message (file errors embed
    /// paths).
    static func failure(_ operation: String, _ error: Error) -> String {
        "\(operation) failed (\(String(describing: type(of: error))))"
    }
}

/// How a shard read went (Windows `ShardLoadResult`).
enum VaultShardLoad: Equatable {
    case ok, missing, corrupt
}

/// File IO for the vault (item 46), a port of Windows `VaultStore`. One directory per recorded
/// Claude Code folder under the vault directory, named by the folder's id (`VaultFolderID`), so
/// erasing a folder's record is one directory delete. Formats and names are the Windows ones:
/// `sessions-YYYY-MM.json`, `rollups-YYYY-MM.json`, `checkpoints.json`, `meta.json`.
///
/// Writes go to a `.tmp` sibling and are renamed over the file (3 tries). A session shard that
/// still fails throws, so the ingester stops before rollups and checkpoints (the write order).
/// Quarantine, ingest side only, renames an unreadable session shard to a timestamped `.bad`,
/// never overwritten and never deleted, and deletes the folder's checkpoints so the next cycle
/// re-reads everything still on disk.
final class VaultStore: @unchecked Sendable {
    static let writeRetries = 3

    let vaultDir: String
    private let log: VaultLogSink?

    init(vaultDir: String, log: VaultLogSink? = nil) {
        self.vaultDir = vaultDir
        self.log = log
    }

    func rootDir(_ root: String) -> String { (vaultDir as NSString).appendingPathComponent(root) }

    private func file(_ root: String, _ name: String) -> String {
        (rootDir(root) as NSString).appendingPathComponent(name)
    }

    func sessionShardPath(_ root: String, _ month: String) -> String { file(root, "sessions-\(month).json") }
    func rollupShardPath(_ root: String, _ month: String) -> String { file(root, "rollups-\(month).json") }
    func checkpointsPath(_ root: String) -> String { file(root, "checkpoints.json") }
    func metaPath(_ root: String) -> String { file(root, "meta.json") }

    /// Lowercase hex SHA-256 of the case-folded absolute path: the checkpoint key, so
    /// `checkpoints.json` is never a readable path ledger. Folded to lowercase as on Windows:
    /// Mac volumes (APFS, HFS+) are case-insensitive unless formatted otherwise, so one file
    /// reached by two spellings keeps one checkpoint, and the key matches what Windows computes
    /// for the same string. On a case-sensitive volume two files differing only by case would
    /// share a key; Claude Code never names logs that way (uuids).
    static func pathKey(_ absolutePath: String) -> String {
        sha256Hex(absolutePath.lowercased())
    }

    static func sha256Hex(_ s: String) -> String {
        sha256Hex(Data(s.utf8))
    }

    static func sha256Hex(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    // MARK: Session shards

    func loadSessionShard(_ root: String, _ month: String) -> (VaultShardLoad, VaultSessionShard) {
        load(sessionShardPath(root, month), as: VaultSessionShard.self) ?? (.missing, VaultSessionShard())
    }

    /// Throws when the replace still fails after the retries: the caller abandons the folder's
    /// cycle, since checkpoints must not move past a shard that never landed.
    func saveSessionShard(_ root: String, _ month: String, _ shard: VaultSessionShard) throws {
        try write(sessionShardPath(root, month), shard, throwOnFailure: true)
    }

    // MARK: Rollups (derived cache)

    func loadRollupShard(_ root: String, _ month: String) -> (VaultShardLoad, VaultRollupShard) {
        load(rollupShardPath(root, month), as: VaultRollupShard.self) ?? (.missing, VaultRollupShard())
    }

    func saveRollupShard(_ root: String, _ month: String, _ shard: VaultRollupShard) {
        try? write(rollupShardPath(root, month), shard, throwOnFailure: false)
    }

    func deleteRollupShard(_ root: String, _ month: String) {
        remove(rollupShardPath(root, month), operation: "rollup-delete")
    }

    // MARK: Checkpoints

    /// An unreadable checkpoints file is disposable: deleted, and empty returned. The full
    /// re-ingest that follows converges.
    func loadCheckpoints(_ root: String) -> VaultCheckpointFile {
        let p = checkpointsPath(root)
        guard FileManager.default.fileExists(atPath: p) else { return VaultCheckpointFile() }
        do {
            let data = try Data(contentsOf: URL(fileURLWithPath: p))
            return try JSONDecoder().decode(VaultCheckpointFile.self, from: data)
        } catch {
            log?(VaultLog.failure("checkpoints-load", error))
            remove(p, operation: "checkpoints-delete")
            return VaultCheckpointFile()
        }
    }

    func deleteCheckpoints(_ root: String) {
        remove(checkpointsPath(root), operation: "checkpoints-delete")
    }

    func saveCheckpoints(_ root: String, _ file: VaultCheckpointFile) {
        try? write(checkpointsPath(root), file, throwOnFailure: false)
    }

    // MARK: Meta

    func loadMeta(_ root: String) -> VaultRootMeta? {
        guard let (result, meta) = load(metaPath(root), as: VaultRootMeta.self), result == .ok else { return nil }
        return meta
    }

    func saveMeta(_ root: String, _ meta: VaultRootMeta) {
        try? write(metaPath(root), meta, throwOnFailure: false)
    }

    // MARK: Listing, quarantine, erase

    /// Months (`yyyy-MM`, ascending) with a session shard. Rollups, `.bad` files, `.tmp` files
    /// and anything misnamed are ignored.
    func listSessionShardMonths(_ root: String) -> [String] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: rootDir(root))) ?? []
        let length = "sessions-YYYY-MM.json".count
        return names.filter { $0.count == length && $0.hasPrefix("sessions-") && $0.hasSuffix(".json") }
            .map { String($0.dropFirst("sessions-".count).prefix(7)) }
            .sorted()
    }

    /// Ingest side only (reads never mutate). Checkpoints go first: a crash between the two
    /// steps leaves the bad shard with no checkpoints, which the next cycle quarantines again;
    /// the reverse would strand checkpoints pointing at a vanished shard.
    func quarantineSessionShard(_ root: String, _ month: String, now: Date) {
        let src = sessionShardPath(root, month)
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyyMMdd'T'HHmmss"
        let stamp = f.string(from: now)
        var dest = "\(src).\(stamp).bad"
        var n = 1
        while FileManager.default.fileExists(atPath: dest) {
            dest = "\(src).\(stamp)-\(n).bad"
            n += 1
        }
        remove(checkpointsPath(root), operation: "quarantine-checkpoints")
        if rename(src, dest) != 0 {
            log?("quarantine failed (POSIXError)")
        }
    }

    /// Deletes one folder's record: shards, rollups, checkpoints, meta and any `.bad` files.
    @discardableResult
    func purgeRoot(_ root: String) -> Bool {
        let dir = rootDir(root)
        guard FileManager.default.fileExists(atPath: dir) else { return true }
        do {
            try FileManager.default.removeItem(atPath: dir)
            return true
        } catch {
            log?(VaultLog.failure("purge-root", error))
            return false
        }
    }

    func rootExists(_ root: String) -> Bool {
        FileManager.default.fileExists(atPath: rootDir(root))
    }

    // MARK: Plumbing

    /// Nil when the file is missing; `.corrupt` when it can't be read or decoded.
    private func load<T: Decodable>(_ path: String, as: T.Type) -> (VaultShardLoad, T)? {
        guard FileManager.default.fileExists(atPath: path) else { return nil }
        do {
            let data = try Data(contentsOf: URL(fileURLWithPath: path))
            return (.ok, try JSONDecoder().decode(T.self, from: data))
        } catch {
            log?(VaultLog.failure("shard-load", error))
            guard let empty = try? JSONDecoder().decode(T.self, from: Data("{}".utf8)) else { return nil }
            return (.corrupt, empty)
        }
    }

    /// Sorted keys: the same contents always give the same bytes (Swift dictionaries have no
    /// stable order), so re-ingesting an unchanged folder rewrites identical files.
    static func encode<T: Encodable>(_ value: T) throws -> Data {
        let e = JSONEncoder()
        e.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try e.encode(value)
    }

    private func write<T: Encodable>(_ path: String, _ value: T, throwOnFailure: Bool) throws {
        let tmp = path + ".tmp"
        var last: Error?
        for _ in 0..<Self.writeRetries {
            do {
                try FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                        withIntermediateDirectories: true)
                let data = try Self.encode(value)
                try data.write(to: URL(fileURLWithPath: tmp))
                guard rename(tmp, path) == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
                return
            } catch {
                last = error
                usleep(25_000)
            }
        }
        unlink(tmp)
        if let last {
            log?(VaultLog.failure("write", last))
            if throwOnFailure { throw last }
        }
    }

    private func remove(_ path: String, operation: String) {
        guard FileManager.default.fileExists(atPath: path) else { return }
        do {
            try FileManager.default.removeItem(atPath: path)
        } catch {
            log?(VaultLog.failure(operation, error))
        }
    }
}

// MARK: Writer lock

/// The vault's single writer across processes: an exclusive `flock` on `vault/.writer.lock`, in
/// place of the Windows named mutex. Locks belong to the open file, so two opens in the same
/// process exclude each other too. The kernel drops the lock when the process dies.
final class VaultWriterLock {
    private var fd: Int32

    private init(fd: Int32) { self.fd = fd }

    deinit { release() }

    private static func open(_ path: String) -> Int32 {
        try? FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                withIntermediateDirectories: true)
        return Darwin.open(path, O_RDWR | O_CREAT | O_CLOEXEC, 0o600)
    }

    /// The lock now, or nil when another holder has it.
    static func tryAcquire(_ path: String) -> VaultWriterLock? {
        let fd = open(path)
        guard fd >= 0 else { return nil }
        guard flock(fd, LOCK_EX | LOCK_NB) == 0 else {
            close(fd)
            return nil
        }
        return VaultWriterLock(fd: fd)
    }

    /// Waits up to `timeout` for the lock.
    static func acquire(_ path: String, timeout: TimeInterval) -> VaultWriterLock? {
        let deadline = Date().addingTimeInterval(timeout)
        while true {
            if let lock = tryAcquire(path) { return lock }
            if Date() >= deadline { return nil }
            usleep(20_000)
        }
    }

    func release() {
        guard fd >= 0 else { return }
        flock(fd, LOCK_UN)
        close(fd)
        fd = -1
    }
}
