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

    /// Atomic, readable by this user only.
    static func writeOwnerOnly(_ data: Data, to url: URL) {
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        guard (try? data.write(to: url, options: .atomic)) != nil else { return }
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }

    /// An ISO 8601 stamp with milliseconds, as the result files carry.
    static func stamp(_ date: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.string(from: date)
    }
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

    /// Adds a subscriber; the watch starts with the first one.
    func add(_ handler: @escaping () -> Void) {
        handlers.append(handler)
        guard watcher == nil else { return }
        watcher = ThemeFolderWatcher(dir: dir) { [weak self] in
            MainActor.assumeIsolated { self?.handlers.forEach { $0() } }
        }
    }
}
