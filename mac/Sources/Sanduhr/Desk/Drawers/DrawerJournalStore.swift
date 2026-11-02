import Foundation
import Darwin

/// The journal file (docs/mac-drawers-setup-spec.md, section 5): owner-only, written atomically.
/// The pure `DrawerJournal` encodes and trims; this reads and writes the file. It takes its URL
/// from the caller on purpose: there is no default parameter, so a test can't reach the real
/// journal by leaving one out. Production asks for `standard()`.
struct DrawerJournalStore {
    let url: URL

    init(url: URL) { self.url = url }

    /// `~/Library/Application Support/Sanduhr/drawers-journal.json`. Never used by a test.
    static func standard() -> DrawerJournalStore {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        return DrawerJournalStore(url: base.appendingPathComponent("Sanduhr/drawers-journal.json"))
    }

    struct WriteError: Error, Equatable { var detail: String }

    /// The saved journal. A missing or unreadable file reads as empty.
    func load() -> DrawerJournal {
        guard let data = try? Data(contentsOf: url) else { return DrawerJournal() }
        return DrawerJournal.decode(String(data: data, encoding: .utf8))
    }

    /// Writes the journal to a sibling temp file with mode 0600, syncs it, then renames it over the
    /// real one, so a crash leaves the old file or the new one and never half of either. The temp
    /// name is fixed, so a failed write leaves at most one stale file that the next save reuses.
    func save(_ journal: DrawerJournal) throws {
        let dir = url.deletingLastPathComponent()
        do { try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700]) }
        catch { throw WriteError(detail: "couldn't make the journal folder") }

        let temp = dir.appendingPathComponent(url.lastPathComponent + ".tmp")
        let fd = open(temp.path, O_WRONLY | O_CREAT | O_TRUNC | O_NOFOLLOW, 0o600)
        guard fd >= 0 else { throw WriteError(detail: "couldn't open the journal for writing") }
        var closed = false
        defer { if !closed { close(fd) } }
        // The mode on open is masked by umask and ignored for a file that already existed.
        guard fchmod(fd, 0o600) == 0 else { throw WriteError(detail: "couldn't set the journal's permissions") }

        let bytes = Array(journal.encoded().utf8)
        var offset = 0
        while offset < bytes.count {
            let n = bytes[offset...].withUnsafeBytes { write(fd, $0.baseAddress, $0.count) }
            if n < 0 { if errno == EINTR { continue }; throw WriteError(detail: "couldn't write the journal") }
            offset += n
        }
        guard fsync(fd) == 0 else { throw WriteError(detail: "couldn't sync the journal") }
        closed = true
        guard close(fd) == 0 else { throw WriteError(detail: "couldn't close the journal") }
        // Replacing the journal is the one overwrite Sanduhr makes, and it is its own file.
        guard rename(temp.path, url.path) == 0 else { throw WriteError(detail: "couldn't put the journal in place") }
    }
}
