import Foundation

/// The one place an item inside a drawer can be put in the Trash, and the check every item action
/// (rename, trash) makes first (docs/mac-drawers-setup-spec.md, section 7). Sanduhr never deletes: a
/// Command-Delete or Move to Trash from the user's own hand is the only way a file leaves a drawer
/// other than a drag, and it goes through `FileManager.trashItem`, so the Finder's Put Back works.
enum DrawerItemGuard {
    /// The item's real path, when it is a direct child of a drawer's folder (owned or view). The
    /// item's folder is resolved and the item itself is not followed, so a symlink stays a
    /// symlink. Nil for anything else: the drawer's own folder, a path outside every drawer, a
    /// path two levels down, a missing item.
    static func entry(_ path: String, drawers: [DrawerDef]) -> (drawer: DrawerDef, path: String)? {
        guard let entry = DrawerFS.resolveEntry(path), DrawerFS.lstat(entry) != nil else { return nil }
        for d in drawers {
            guard let home = DrawerFS.resolve(d.home) else { continue }
            if DrawerPaths.isChild(entry, of: home) { return (d, entry) }
        }
        return nil
    }
}

enum DrawerTrash {
    /// What the real Trash does: a move that Finder can put back.
    static let moveToTrash: (URL) throws -> Void = { url in
        try FileManager.default.trashItem(at: url, resultingItemURL: nil)
    }

    /// Moves one item to the Trash, only when it sits directly inside a drawer's folder. Returns
    /// why it did not, or nil. The only call to `trashItem` in the drawer code.
    @discardableResult
    static func trash(_ path: String, drawers: [DrawerDef], using trasher: (URL) throws -> Void = moveToTrash) -> String? {
        guard let found = DrawerItemGuard.entry(path, drawers: drawers) else { return "That item isn't in a drawer." }
        do {
            try trasher(URL(fileURLWithPath: found.path))
            return nil
        } catch {
            return "It couldn't be moved to the Trash."
        }
    }
}
