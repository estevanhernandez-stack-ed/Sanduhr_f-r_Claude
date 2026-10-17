import Foundation

/// The request/result handoffs between the Sanduhr MCP server and the app (item 54's Desk
/// messages, item 55's themes): the server drops a request file into Sanduhr's Application
/// Support folder, the app reads and removes it, checks it, and answers in a result file the
/// server waits for. This is the part they share: one watch on the folder for every request, and
/// the owner-only atomic writes.
enum HandoffFiles {
    /// ~/Library/Application Support/Sanduhr.
    static var support: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Sanduhr", isDirectory: true)
    }

    /// Reads a waiting request and removes it, so it is handled once. Nil when there is none, it
    /// cannot be read, or it is larger than `maxBytes` (removed all the same: it is never valid).
    static func take(_ url: URL, maxBytes: Int = 256 * 1024) -> Data? {
        let fm = FileManager.default
        guard fm.fileExists(atPath: url.path) else { return nil }
        let size = (try? fm.attributesOfItem(atPath: url.path)[.size] as? Int) ?? 0
        let data = size <= maxBytes ? try? Data(contentsOf: url) : nil
        try? fm.removeItem(at: url)
        return data
    }

    /// Atomic, readable by this user only: a temporary sibling created with mode 0600 and renamed
    /// over `url`, so a reader never sees half of it and the bytes are never wider than 0600, not
    /// even between write and chmod. The parent folder is made when missing. After Reset Sanduhr…
    /// ran it silently writes nothing and doesn't throw; there is no opt-out, so a writer that is
    /// part of the reset itself (the credential stores, the installer) keeps its own write.
    static func writeOwnerOnlyOrThrow(_ data: Data, to url: URL) throws {
        // Reset Sanduhr… ran: nothing goes back into the folders it emptied.
        guard !ResetGate.isOn else { return }
        let fm = FileManager.default
        let dir = url.deletingLastPathComponent()
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let tmp = dir.appendingPathComponent(".\(url.lastPathComponent).\(UUID().uuidString).tmp")
        guard fm.createFile(atPath: tmp.path, contents: data, attributes: [.posixPermissions: 0o600]) else {
            throw CocoaError(.fileWriteUnknown)
        }
        guard Darwin.rename(tmp.path, url.path) == 0 else {
            let code = errno
            unlink(tmp.path)
            throw POSIXError(POSIXErrorCode(rawValue: code) ?? .EIO)
        }
    }

    /// `writeOwnerOnlyOrThrow` with the failure dropped: for the handoffs, where a result the
    /// server can't read is the same as none.
    static func writeOwnerOnly(_ data: Data, to url: URL) {
        try? writeOwnerOnlyOrThrow(data, to: url)
    }

    /// An ISO 8601 stamp with milliseconds, as the result files carry.
    static func stamp(_ date: Date) -> String { ISO8601.stamp(date) }
}

/// One kernel watch on Sanduhr's folder (the themes folder's watcher, no polling) for every
/// handoff: each subscriber checks for its own request file on any event.
@MainActor
final class HandoffWatch {
    static let shared = HandoffWatch(dir: HandoffFiles.support)

    let dir: URL
    private var watcher: ThemeFolderWatcher?
    private var handlers: [() -> Void] = []

    init(dir: URL) { self.dir = dir }

    /// Reset Sanduhr… (slice 5): no request is picked up any more, and the folder is not watched.
    func stop() {
        stopped = true
        handlers = []
        watcher?.stop()
        watcher = nil
    }

    private var stopped = false
    var isStopped: Bool { stopped && watcher == nil }

    /// Adds a subscriber; the watch starts with the first one.
    func add(_ handler: @escaping () -> Void) {
        guard !stopped else { return }
        handlers.append(handler)
        guard watcher == nil else { return }
        watcher = ThemeFolderWatcher(dir: dir) { [weak self] in
            MainActor.assumeIsolated { self?.handlers.forEach { $0() } }
        }
    }
}
