import Foundation

/// Live Claude Code activity (item 45): the local burn the widget's cards show as "+Nk", the
/// tokens Claude Code used since the last meter refresh. claude.ai's numbers lag by minutes and
/// the widget asks every 5; the badge covers both lags (Windows `SetLocalDelta`).
///
/// Only the active account's linked folder is read, and only when its activity is Live only or
/// Keep a record. Not tracked, or no folder, touches nothing on disk. Nothing is stored: the
/// reader's cache lives in memory and goes with a change of folder.
enum LocalActivity {
    /// The folder to read for these choices, or nil.
    static func folder(for choices: AccountDataChoices) -> String? {
        guard choices.activity != .off, let f = choices.folder, !f.isEmpty else { return nil }
        return f
    }
}

/// One scan's answer for the cards and state.yaml: tokens by tier key and the events counted.
/// Never a path, a project or a model name.
struct LocalBurn: Equatable, Sendable {
    var byTier: [String: Int64] = [:]
    var total: Int64 = 0
    var events = 0

    /// The badge's tokens for a card, zero when nothing burned there.
    func tokens(for tier: Tier) -> Int64 { byTier[tier.rawValue] ?? 0 }
}

/// Runs the reader for whichever folder the active account's choices name, keeping one reader
/// (and its per-file cache) while the folder stays the same. Call off the main thread.
final class LocalBurnSource: @unchecked Sendable {
    private let fs: CCLogFileSystem
    private let lock = NSLock()
    private var reader: CCLogReader?

    init(fileSystem: CCLogFileSystem = LocalCCLogFileSystem()) {
        self.fs = fileSystem
    }

    /// The burn since `since` in the folder `choices` allow, or nil when nothing may be read (and
    /// then nothing was). A different folder starts a fresh reader.
    func scan(_ choices: AccountDataChoices, since: Date) -> LocalBurn? {
        guard let folder = LocalActivity.folder(for: choices) else {
            drop()
            return nil
        }
        lock.lock()
        let r: CCLogReader
        if let current = reader, current.root == folder {
            r = current
        } else {
            r = CCLogReader(root: folder, fileSystem: fs)
            reader = r
        }
        lock.unlock()
        let b = r.burnSince(since)
        return LocalBurn(byTier: b.byTier, total: b.total, events: b.events)
    }

    /// Forgets the reader and its cache.
    func drop() {
        lock.lock()
        reader = nil
        lock.unlock()
    }
}
