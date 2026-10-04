import CryptoKit
import Foundation

/// The Claude Code integration scripts (item 49): shipped inside the app at
/// `Contents/Resources/integrations/`, copied to `~/Library/Application Support/Sanduhr/integrations/`
/// for Claude Code to run.
///
/// **Stamped folders behind one stable link.** Each set of scripts lands in a folder named for
/// its stamp (the first 12 hex digits of a SHA-256 over the scripts' names and bytes), and
/// `integrations/current` is a symlink to the folder in use. Claude Code's settings name
/// `…/integrations/current/<script>`, so an app update never rewrites anyone's config: it adds a
/// new stamped folder and swaps the link. This is Windows' versioned-folder answer
/// (`McpIntegrationInstaller`): there a running session pins the exe, so a new version goes into
/// a new folder; here nothing is locked, but a session starting mid-update must never read half
/// a script, and a new folder plus one `rename(2)` of the link means it sees the old set or the
/// new one, whole. The stamp follows the scripts' content, so an update that leaves them alone
/// changes nothing on disk.
///
/// The folder swapped out stays until the next refresh (a session that resolved the link a
/// moment before the swap still finds it); every older stamped folder is deleted. Python reads a
/// script whole when it starts, so a running server or statusline never needs its folder again.
/// The flat copies `install.sh` makes beside them are never touched.
struct IntegrationScripts {
    static let mcpScript = "sanduhr_mcp.py"
    static let statuslineScript = "sanduhr_statusline.py"
    static let names = [mcpScript, statuslineScript]
    static let currentName = "current"

    /// The scripts as the app ships them; nil where they are missing (a `swift run` build).
    let source: URL?
    /// `…/Application Support/Sanduhr/integrations`.
    let dir: URL

    static var standard: IntegrationScripts {
        IntegrationScripts(
            source: Bundle.main.resourceURL?.appendingPathComponent("integrations", isDirectory: true),
            dir: HistoryStore.Files.standard.dir.appendingPathComponent("integrations", isDirectory: true))
    }

    enum Failure: Error, Equatable {
        /// The app has no scripts inside (a development build run without build.sh).
        case missingFromApp
        case writeFailed
    }

    /// The stable link Claude Code's settings name.
    var current: URL { dir.appendingPathComponent(Self.currentName) }

    /// The path written into Claude Code's settings for `name`.
    func installedPath(_ name: String) -> String { current.appendingPathComponent(name).path }

    // MARK: Stamps

    /// The stamp of a folder holding the scripts, or nil when one is missing.
    static func stamp(of folder: URL) -> String? {
        var hasher = SHA256()
        for name in names {
            guard let data = try? Data(contentsOf: folder.appendingPathComponent(name)) else { return nil }
            hasher.update(data: Data(name.utf8))
            hasher.update(data: Data([0]))
            hasher.update(data: data)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined().prefix(12).description
    }

    /// The stamp of the scripts inside the app.
    var bundledStamp: String? { source.flatMap(Self.stamp(of:)) }

    /// The stamp `current` points at, or nil without a link.
    var installedStamp: String? {
        guard let target = try? FileManager.default.destinationOfSymbolicLink(atPath: current.path) else { return nil }
        return (target as NSString).lastPathComponent
    }

    /// An install exists: the link is there (the launch refresh runs only then).
    var isInstalled: Bool { installedStamp != nil }

    /// The installed scripts match the app's.
    var isCurrent: Bool { bundledStamp != nil && installedStamp == bundledStamp }

    static func isStampName(_ name: String) -> Bool {
        name.count == 12 && name.allSatisfy { $0.isHexDigit && !$0.isUppercase }
    }

    // MARK: Refresh

    /// Puts the app's scripts in their stamped folder, points `current` at it and deletes
    /// stamped folders no longer in use. Returns the stamp.
    @discardableResult
    func refresh() throws -> String {
        guard let source, let stamp = Self.stamp(of: source) else { throw Failure.missingFromApp }
        let fm = FileManager.default
        do {
            try fm.createDirectory(at: dir, withIntermediateDirectories: true)
            let target = dir.appendingPathComponent(stamp, isDirectory: true)
            if Self.stamp(of: target) != stamp {
                // Missing or altered: build it beside, then move it into place whole.
                let staging = dir.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
                try fm.createDirectory(at: staging, withIntermediateDirectories: true)
                for name in Self.names {
                    let dest = staging.appendingPathComponent(name)
                    try fm.copyItem(at: source.appendingPathComponent(name), to: dest)
                    try fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: dest.path)
                }
                try? fm.removeItem(at: target)
                try fm.moveItem(at: staging, to: target)
            }
            let replaced = installedStamp
            if replaced != stamp {
                let link = dir.appendingPathComponent(".current-\(UUID().uuidString)")
                try fm.createSymbolicLink(atPath: link.path, withDestinationPath: stamp)
                guard rename(link.path, current.path) == 0 else {
                    unlink(link.path)
                    throw Failure.writeFailed
                }
            }
            prune(keep: [stamp, replaced].compactMap { $0 })
            return stamp
        } catch let f as Failure {
            NSLog("Sanduhr integrations refresh failed (\(f))")
            throw f
        } catch {
            NSLog("Sanduhr integrations refresh failed (\(type(of: error)))")
            throw Failure.writeFailed
        }
    }

    /// The launch refresh: only where an install exists. Failures are logged by type only.
    func refreshIfInstalled() {
        guard isInstalled else { return }
        _ = try? refresh()
    }

    /// Deletes stamped folders other than `keep`, and leftovers of an interrupted refresh.
    func prune(keep: [String]) {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else { return }
        for name in names where !keep.contains(name) {
            if Self.isStampName(name) || name.hasPrefix(".staging-") || name.hasPrefix(".current-") {
                try? fm.removeItem(at: dir.appendingPathComponent(name))
            }
        }
    }

    /// Removes the link and every stamped folder (nothing is installed in any folder any more).
    func removeAll() {
        unlink(current.path)
        prune(keep: [])
        let fm = FileManager.default
        if let left = try? fm.contentsOfDirectory(atPath: dir.path), left.isEmpty {
            try? fm.removeItem(at: dir)
        }
    }
}
