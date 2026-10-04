import Foundation

/// One record on this Mac, as the Claude Usage page's stewardship list shows it: its vault id,
/// size on disk, the oldest day it holds, and the account whose linked folder it records, or
/// none ("not linked to an account": the folder was unlinked, the account removed, or the link
/// moved). The id is not reversible to the folder; `folder` is known only through a link.
struct VaultRecordInfo: Identifiable, Equatable, Sendable {
    let id: String
    let bytes: Int64
    let oldestDay: CCLocalDay?
    /// The account linked to the recorded folder, nil when none is.
    let account: String?
    /// That account's linked folder, nil when not linked.
    let folder: String?
    /// The linked account keeps a record (erase turns it to Live only first).
    let recording: Bool

    var isLinked: Bool { account != nil }

    /// A short handle for a record no account names: the id's first 8 digits.
    var shortID: String { String(id.prefix(8)) }
}

/// The stewardship list (item 48): every `vault/<id>` folder, whichever account it belongs to,
/// so a record left behind when its folder was unlinked can still be seen and erased. Reads
/// directory sizes and one small file per record; never writes.
enum VaultStewardship {
    /// A vault directory name the app may erase: one path component, never hidden, `.` or `..`.
    static func isRecordID(_ id: String) -> Bool {
        !id.isEmpty && !id.contains("/") && !id.hasPrefix(".")
    }

    /// Linked records first, by account (case-insensitively), then the unlinked ones by id.
    static func records(in store: VaultStore, choices: [String: AccountDataChoices]) -> [VaultRecordInfo] {
        let fm = FileManager.default
        let names = (try? fm.contentsOfDirectory(atPath: store.vaultDir)) ?? []
        var links: [String: (account: String, folder: String, recording: Bool)] = [:]
        for (account, c) in choices.sorted(by: { $0.key < $1.key }) {
            guard let folder = c.folder, !folder.isEmpty else { continue }
            let id = VaultFolderID.of(folder)
            if links[id] == nil { links[id] = (account, folder, c.activity == .record) }
        }
        var out: [VaultRecordInfo] = []
        for name in names where isRecordID(name) {
            var isDir: ObjCBool = false
            let dir = store.rootDir(name)
            guard fm.fileExists(atPath: dir, isDirectory: &isDir), isDir.boolValue else { continue }
            let link = links[name]
            out.append(VaultRecordInfo(id: name, bytes: size(of: dir), oldestDay: oldestDay(store, name),
                                       account: link?.account, folder: link?.folder,
                                       recording: link?.recording ?? false))
        }
        return out.sorted { a, b in
            switch (a.account, b.account) {
            case let (x?, y?):
                let order = x.localizedCaseInsensitiveCompare(y)
                return order == .orderedSame ? a.id < b.id : order == .orderedAscending
            case (.some, nil): return true
            case (nil, .some): return false
            case (nil, nil): return a.id < b.id
            }
        }
    }

    /// Bytes of every file under `dir`, `.bad` and `.tmp` files included.
    static func size(of dir: String) -> Int64 {
        guard let walker = FileManager.default.enumerator(atPath: dir) else { return 0 }
        var total: Int64 = 0
        while walker.nextObject() != nil {
            let attrs = walker.fileAttributes
            guard attrs?[.type] as? FileAttributeType == .typeRegular else { continue }
            total += (attrs?[.size] as? NSNumber)?.int64Value ?? 0
        }
        return total
    }

    /// The oldest day the record holds: the earliest day in its earliest session month (the
    /// rollups when present, else the session shard itself), else the record's birth date.
    static func oldestDay(_ store: VaultStore, _ id: String) -> CCLocalDay? {
        if let month = store.listSessionShardMonths(id).first {
            let (rollupStatus, rollups) = store.loadRollupShard(id, month)
            if rollupStatus == .ok, let key = rollups.days.keys.min(), let day = CCLocalDay(key: key) {
                return day
            }
            let (status, shard) = store.loadSessionShard(id, month)
            if status == .ok, let key = shard.sessions.values.flatMap(\.byDay.keys).min(),
               let day = CCLocalDay(key: key) {
                return day
            }
        }
        return store.loadMeta(id).flatMap { CCLocalDay(key: $0.since) }
    }

    /// "1.2 MB", as Finder counts.
    static func sizeText(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
