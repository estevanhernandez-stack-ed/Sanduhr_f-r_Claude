import Foundation

/// Undo for one journal entry and the return that turning Drawers off runs
/// (docs/mac-drawers-setup-spec.md, section 5). Pure: what exists, and when it was last modified,
/// come in through closures, so nothing here touches the file system. Mirrors Windows'
/// `DrawerUndo` and `DrawerReturn`.

/// A step back: move the file at `from` to `to`.
struct UndoStep: Equatable, Sendable {
    var from: String
    var to: String
}

/// A file Undo leaves where it is, and why.
struct UndoSkip: Equatable, Sendable {
    var path: String
    var reason: String
}

struct UndoPlan: Equatable, Sendable {
    var steps: [UndoStep]
    var skipped: [UndoSkip]
}

enum DrawerUndo {
    static let movedSince = "was moved since"
    static let changedSince = "changed since"
    static let noFreeName = "no free name"
    /// A modification date within this of the recorded one counts as unchanged: the journal keeps
    /// seven fractional digits, finer than a `Date` holds.
    static let dateTolerance: TimeInterval = 0.001

    /// Every written move back where it came from, newest first. A file that is gone, or whose
    /// modification date differs, is skipped and named; a folder skips the date check. A taken old
    /// place gets the next free name, so Undo never overwrites. A move that never got its flag
    /// (a crash) is left out.
    static func plan(_ entry: JournalEntry, modified: (String) -> Date?, exists: (String) -> Bool) -> UndoPlan {
        var steps: [UndoStep] = []
        var skipped: [UndoSkip] = []
        var claimed = Set<String>()
        for m in entry.writtenMoves.reversed() {
            guard let now = modified(m.to) else { skipped.append(UndoSkip(path: m.to, reason: movedSince)); continue }
            if !m.isFolder, case .done(let was?) = m.written, abs(now.timeIntervalSince(was)) > dateTolerance {
                skipped.append(UndoSkip(path: m.to, reason: changedSince))
                continue
            }
            let dir = DrawerPaths.parent(m.from)
            let name = String(m.from.split(separator: "/").last ?? "")
            let taken: (String) -> Bool = { cand in
                let p = DrawerPaths.join(dir, cand)
                return exists(p) || claimed.contains(DrawerPaths.key(p))
            }
            guard let free = DrawerNames.free(name, isFolder: m.isFolder, exists: taken) else {
                skipped.append(UndoSkip(path: m.to, reason: noFreeName))
                continue
            }
            let back = DrawerPaths.join(dir, free)
            claimed.insert(DrawerPaths.key(back))
            steps.append(UndoStep(from: m.to, to: back))
        }
        return UndoPlan(steps: steps, skipped: skipped)
    }
}

/// One thing sitting in a drawer's folder, as the app listed it.
struct DrawerChild: Equatable, Sendable {
    var path: String
    var isFolder: Bool
}

/// The moves back to the Desktop, and the folders to remove once each is empty (homes, then the root).
struct ReturnPlan: Equatable, Sendable {
    var moves: [UndoStep]
    var foldersToRemove: [String]
}

enum DrawerReturn {
    /// Empties every owned drawer whose home sits directly in the root onto the Desktop, files and
    /// folders alike, whether the journal moved them or the user dragged them in. A view drawer
    /// keeps its contents. The mover removes a folder only once it is empty, so one that kept
    /// something stays listed. `removeRoot` is false for a root the user picked: only the default
    /// root goes. A root that is the Desktop or holds it plans nothing.
    static func plan(drawers: [DrawerDef], root: String, desktop: String,
                     childrenOf: (String) -> [DrawerChild], exists: (String) -> Bool,
                     removeRoot: Bool) -> ReturnPlan {
        let empty = ReturnPlan(moves: [], foldersToRemove: [])
        guard let root = DrawerPaths.normalize(root), let desk = DrawerPaths.normalize(desktop),
              !DrawerPaths.isInside(desk, root) else { return empty }
        var moves: [UndoStep] = []
        var homes: [String] = []
        var claimed = Set<String>()
        for d in drawers {
            guard d.owned, let home = DrawerPaths.normalize(d.home), DrawerPaths.isChild(home, of: root) else { continue }
            let kids = childrenOf(home).sorted { DrawerPaths.key($0.path) < DrawerPaths.key($1.path) }
            for item in kids {
                let name = String(item.path.split(separator: "/").last ?? "")
                let taken: (String) -> Bool = { cand in
                    let p = DrawerPaths.join(desk, cand)
                    return exists(p) || claimed.contains(DrawerPaths.key(p))
                }
                // No free name after 99 tries: leave the item in its drawer; the folder then stays.
                guard let free = DrawerNames.free(name, isFolder: item.isFolder, exists: taken) else { continue }
                let to = DrawerPaths.join(desk, free)
                claimed.insert(DrawerPaths.key(to))
                moves.append(UndoStep(from: item.path, to: to))
            }
            homes.append(home)
        }
        if !homes.isEmpty && removeRoot { homes.append(root) }
        return ReturnPlan(moves: moves, foldersToRemove: homes)
    }

    /// The confirmation's question, with the count.
    static func prompt(_ count: Int) -> String {
        count == 1 ? "Put 1 file back on the Desktop?" : "Put \(count) files back on the Desktop?"
    }
}
