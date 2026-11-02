import Foundation
import Darwin

/// The mover (docs/mac-drawers-setup-spec.md, sections 4 and 5): the one place Sanduhr touches a
/// user's files. It executes a checked plan, an Undo and the return against real folders, and it
/// has exactly one way to put a file anywhere: `renamex_np(from, to, RENAME_EXCL)`. That call is
/// atomic on one volume and refuses, in the kernel, to replace what is already there. So there is
/// no copy, no overwrite and no delete here; the only removals are `rmdir` on a drawer folder that
/// is empty at that moment, and a lone `.DS_Store` that Finder left in it.
///
/// Every location comes in through `DrawerLocations`. Nothing defaults to the real Desktop, so a
/// test hands it a temp folder and cannot reach anything else.

/// Where the Desktop, the Drawers root and the home folder are. All three are required, with no
/// default for any of them.
struct DrawerLocations: Equatable, Sendable {
    var desktop: String
    var root: String
    var home: String
}

struct DrawerMoved: Equatable, Sendable {
    var from: String
    var to: String
    var isFolder: Bool
}

/// What a run did, for the result bar: what moved, and each file left alone with its reason.
struct DrawerRunResult: Equatable, Sendable {
    /// The journal entry this run wrote (an apply). Nil for an Undo or a return.
    var entryId: String?
    var moved: [DrawerMoved] = []
    var skipped: [UndoSkip] = []
    /// Set when the whole run was refused before anything moved (an unsafe root, a plan outside
    /// the roots, a journal that can't be written).
    var refusal: String?
    /// The return: folders removed because they were empty, and ones that kept something.
    var foldersRemoved: [String] = []
    var foldersKept: [UndoSkip] = []
    /// The final journal write failed; the files moved, and the entry on disk still lists them.
    var journalError: String?
    /// Files whose landing place could not be read back by inode.
    var unverified = 0
    /// A test hook stopped the run, as a crash would.
    var halted = false
}

/// Why a file was left where it is. These are shown to the user, so none holds a name.
enum DrawerSkipReason {
    static let gone = "no longer there"
    static let link = "a link, left alone"
    static let outside = "outside the Desktop and the drawers"
    static let holdsDrawer = "holds a drawer's folder"
    static let changed = "changed since the review"
    static let notPlain = "not a plain file"
    static let inICloud = "in iCloud, not downloaded"
    static let noReview = "not in the review"
    static let otherDisk = "on another disk"
    static let noSafeMove = "this disk can't move files safely"
    static let noFolder = "its folder is gone"
    static let alreadyThere = "already there"
    static let viewDrawer = "a view drawer is only shown, never filled"
    static let noDrawerFolder = "couldn't make the drawer's folder"
    static let stillHolds = "still holds files"

    static func failed(_ code: Int32) -> String {
        switch code {
        case EXDEV: return otherDisk
        case ENOTSUP, ENOSYS: return noSafeMove
        case EACCES, EPERM, EROFS: return "not allowed"
        default: return "couldn't move it (\(String(cString: strerror(code))))"
        }
    }
}

struct DrawerMover {
    /// Moves between journal saves during a run.
    static let saveEvery = 25

    var store: DrawerJournalStore
    /// The one way a file moves. Returns 0, or the errno. Injectable so a test can produce EXDEV.
    var rename: (String, String) -> Int32 = DrawerMover.exclusiveRename
    var now: () -> Date = { Date() }
    /// A test seam: called after the Nth rename of a run and before its journal flag flips. Return
    /// true to stop dead, as a crash would, with no further save.
    var afterRename: ((Int) -> Bool)?

    init(store: DrawerJournalStore, rename: @escaping (String, String) -> Int32 = DrawerMover.exclusiveRename,
         now: @escaping () -> Date = { Date() }, afterRename: ((Int) -> Bool)? = nil) {
        self.store = store
        self.rename = rename
        self.now = now
        self.afterRename = afterRename
    }

    /// `renamex_np` with `RENAME_EXCL`: never replaces an existing destination. There is no plain
    /// `rename` fallback; a disk that can't do this refuses (ENOTSUP) and the file stays.
    static func exclusiveRename(_ from: String, _ to: String) -> Int32 {
        renamex_np(from, to, UInt32(RENAME_EXCL)) == 0 ? 0 : errno
    }

    // MARK: Apply

    /// Runs a plan. `existing` are the drawers already set up; `planned` holds the facts the review
    /// showed, by source path, which a rule move must still match (same size and date). A hand move
    /// (`plan.source == .user`) needs no facts. The journal entry is written before the first move.
    func apply(_ plan: DrawerPlan, at loc: DrawerLocations, existing: [DrawerDef],
               planned: [String: DrawerItemFacts] = [:]) -> DrawerRunResult {
        var result = DrawerRunResult()
        guard let desktop = DrawerFS.resolve(loc.desktop), let root = DrawerFS.resolve(loc.root),
              let home = DrawerFS.resolve(loc.home) else {
            result.refusal = "The Desktop and the Drawers folder must be full paths."
            return result
        }
        let roots = DrawerRoots(desktop: desktop, drawersRoot: root, home: home,
                                desktopVolume: DrawerFS.volumeID(desktop), rootVolume: DrawerFS.volumeID(root))

        // Resolve first, validate the resolved plan: the validator compares strings, so a symlinked
        // folder would otherwise pass as a Desktop child.
        func resolved(_ d: DrawerDef) -> DrawerDef {
            var r = d
            r.home = DrawerFS.resolve(d.home) ?? d.home
            return r
        }
        let existingR = existing.map(resolved)
        let resolvedPlan = DrawerPlan(
            source: plan.source,
            drawers: plan.drawers.map(resolved),
            moves: plan.moves.map { DrawerMove(source: DrawerFS.resolveEntry($0.source) ?? $0.source, drawerId: $0.drawerId) })
        let check = DrawerPlanValidator.check(resolvedPlan, roots: roots, existing: existingR)
        guard check.ok else {
            result.refusal = check.error ?? "The plan was refused."
            return result
        }

        var known: [String: DrawerDef] = [:]
        for d in existingR + check.drawers where known[d.id] == nil { known[d.id] = d }
        guard let places = Places.make(desktop: desktop, root: root, drawers: Array(known.values)) else {
            result.refusal = "The Desktop and the Drawers folder must be full paths."
            return result
        }

        let plannedByKey = Dictionary(planned.compactMap { (path, facts) -> (String, DrawerItemFacts)? in
            DrawerFS.resolveEntry(path).map { (DrawerPaths.key($0), facts) }
        }, uniquingKeysWith: { a, _ in a })

        struct Work { var move: DrawerMove; var dest: String; var index: Int }
        var work: [Work] = []
        var jmoves: [JournalMove] = []
        for m in check.moves {
            // Only an owned drawer directly in the root is ever filled.
            guard known[m.drawerId]?.owned == true, places.homes[m.drawerId] != nil else {
                let reason = known[m.drawerId]?.owned == false ? DrawerSkipReason.viewDrawer : DrawerSkipReason.noDrawerFolder
                result.skipped.append(UndoSkip(path: m.source, reason: reason))
                continue
            }
            guard let st = DrawerFS.lstat(m.source) else {
                result.skipped.append(UndoSkip(path: m.source, reason: DrawerSkipReason.gone))
                continue
            }
            let dest = places.homes[m.drawerId]!
            work.append(Work(move: m, dest: dest, index: jmoves.count))
            jmoves.append(JournalMove(from: m.source, to: DrawerPaths.join(dest, DrawerFS.name(of: m.source)),
                                      isFolder: DrawerFS.isDirectory(st), written: .pending, inode: DrawerFS.inode(st)))
        }
        guard !work.isEmpty else { return result }

        // The entry goes in before the first move. If it can't be written, nothing moves.
        let entryId = UUID().uuidString
        var entry = JournalEntry(id: entryId, at: now(), source: plan.source, moves: jmoves)
        var journal = store.load().adding(entry, now: now())
        do { try store.save(journal) } catch {
            result.refusal = "Sanduhr couldn't write its journal, so nothing was moved."
            result.skipped = []
            return result
        }
        result.entryId = entryId

        var renames = 0
        var sinceSave = 0
        var landed = Set<Int>()
        // A drawer's folder is made when its first file is about to go in, after the journal has the
        // entry, so a run that can't journal leaves no empty folders behind either.
        var folderReady: [String: Bool] = [:]
        for w in work {
            if folderReady[w.move.drawerId] == nil { folderReady[w.move.drawerId] = ensureFolder(w.dest, root: root) }
            guard folderReady[w.move.drawerId] == true else {
                result.skipped.append(UndoSkip(path: w.move.source, reason: DrawerSkipReason.noDrawerFolder))
                continue
            }
            let key = DrawerFS.resolveEntry(w.move.source).map(DrawerPaths.key) ?? ""
            let request = MoveRequest(source: w.move.source, destDir: w.dest,
                                      destName: DrawerFS.name(of: w.move.source),
                                      rule: plan.source == .rules, expect: plannedByKey[key])
            switch attempt(request, places: places) {
            case .skipped(let reason):
                result.skipped.append(UndoSkip(path: w.move.source, reason: reason))
            case .moved(let m):
                renames += 1
                if afterRename?(renames) == true { result.halted = true; return result }
                entry.moves[w.index] = JournalMove(from: entry.moves[w.index].from, to: m.to, isFolder: m.isFolder,
                                                   written: .done(modified: m.isFolder ? nil : m.modified), inode: m.inode)
                landed.insert(w.index)
                result.moved.append(DrawerMoved(from: m.from, to: m.to, isFolder: m.isFolder))
                if !m.verified { result.unverified += 1 }
                sinceSave += 1
                if sinceSave >= Self.saveEvery {
                    journal = journal.updating(entry)
                    // A failed periodic save is not fatal: the entry on disk still lists every move as pending.
                    if (try? store.save(journal)) != nil { sinceSave = 0 }
                }
            }
        }

        // The end: only what moved stays in the entry. Skipped moves would read as an open entry.
        entry.moves = entry.moves.enumerated().filter { landed.contains($0.offset) }.map(\.element)
        journal = entry.moves.isEmpty ? journal.without(entryId) : journal.updating(entry)
        do { try store.save(journal) } catch {
            result.journalError = "Sanduhr couldn't save its journal; Undo may not list this run."
        }
        if entry.moves.isEmpty { result.entryId = nil }
        return result
    }

    // MARK: Undo

    /// Puts an entry's written moves back, newest first. A moved-since or changed-since file is
    /// skipped (that is `DrawerUndo`'s call); a taken old place gets the next free name. The entry
    /// is removed from the journal afterward, since the run is over either way.
    func undo(_ entry: JournalEntry, at loc: DrawerLocations, drawers: [DrawerDef]) -> DrawerRunResult {
        var result = DrawerRunResult()
        guard let places = safePlaces(loc, drawers, &result) else { return result }
        let plan = DrawerUndo.plan(entry, modified: DrawerFS.modified, exists: DrawerFS.exists)
        result.skipped = plan.skipped
        for step in plan.steps {
            let dir = DrawerFS.resolve(DrawerPaths.parent(step.to)) ?? ""
            guard places.allows(dir) else {
                result.skipped.append(UndoSkip(path: step.from, reason: DrawerSkipReason.outside))
                continue
            }
            let request = MoveRequest(source: step.from, destDir: dir, destName: DrawerFS.name(of: step.to), rule: false, expect: nil)
            switch attempt(request, places: places) {
            case .skipped(let reason): result.skipped.append(UndoSkip(path: step.from, reason: reason))
            case .moved(let m):
                result.moved.append(DrawerMoved(from: m.from, to: m.to, isFolder: m.isFolder))
                if !m.verified { result.unverified += 1 }
            }
        }
        do { try store.save(store.load().without(entry.id)) } catch {
            result.journalError = "Sanduhr couldn't save its journal."
        }
        return result
    }

    // MARK: Return

    /// Turning Drawers off: empties every owned drawer onto the Desktop, then removes each folder
    /// that is empty at that moment (the homes, then the root when `removeRoot`). It does not read
    /// the journal. A file that can't go back stays, and so does its folder.
    func returnAll(drawers: [DrawerDef], at loc: DrawerLocations, removeRoot: Bool) -> DrawerRunResult {
        var result = DrawerRunResult()
        guard let places = safePlaces(loc, drawers, &result) else { return result }
        let owned = drawers.filter { $0.owned }.compactMap { d -> DrawerDef? in
            guard places.homes[d.id] != nil else { return nil }
            var r = d
            r.home = places.homes[d.id]!
            return r
        }
        let plan = DrawerReturn.plan(drawers: owned, root: places.root, desktop: places.desktop,
                                     childrenOf: Self.children, exists: DrawerFS.exists, removeRoot: removeRoot)
        for step in plan.moves {
            let request = MoveRequest(source: step.from, destDir: places.desktop, destName: DrawerFS.name(of: step.to),
                                      rule: false, expect: nil)
            switch attempt(request, places: places) {
            case .skipped(let reason): result.skipped.append(UndoSkip(path: step.from, reason: reason))
            case .moved(let m):
                result.moved.append(DrawerMoved(from: m.from, to: m.to, isFolder: m.isFolder))
                if !m.verified { result.unverified += 1 }
            }
        }
        let removable = places.homeKeys.union([DrawerPaths.key(places.root)])
        for folder in plan.foldersToRemove {
            switch removeIfEmpty(folder, removable: removable) {
            case .removed: result.foldersRemoved.append(folder)
            case .gone: break
            case .kept(let reason): result.foldersKept.append(UndoSkip(path: folder, reason: reason))
            }
        }
        return result
    }

    /// What a drawer holds, for the return's plan. `.DS_Store` is Finder's, not the user's, so it
    /// is not a file to put back.
    static func children(_ dir: String) -> [DrawerChild] {
        (DrawerFS.names(in: dir) ?? []).filter { $0 != ".DS_Store" }.map { name in
            let path = DrawerPaths.join(dir, name)
            let st = DrawerFS.lstat(path)
            return DrawerChild(path: path, isFolder: st.map { DrawerFS.isDirectory($0) } ?? false)
        }
    }

    enum RemoveOutcome: Equatable { case removed, gone, kept(String) }

    /// Removes a drawer folder only if nothing is in it right now. The only thing cleared first is
    /// a lone `.DS_Store`, the file Finder drops into a folder it has opened. `rmdir` itself also
    /// refuses a folder that isn't empty, so a file that lands in between keeps its folder.
    func removeIfEmpty(_ path: String, removable: Set<String>) -> RemoveOutcome {
        guard let st = DrawerFS.lstat(path) else { return .gone }
        guard DrawerFS.isDirectory(st), removable.contains(DrawerPaths.key(path)),
              let real = DrawerFS.realPath(path), DrawerPaths.key(real) == DrawerPaths.key(path) else {
            return .kept(DrawerSkipReason.outside)
        }
        var names = DrawerFS.names(in: path) ?? ["?"]
        if names == [".DS_Store"], let s = DrawerFS.lstat(DrawerPaths.join(path, ".DS_Store")), DrawerFS.isRegular(s) {
            if unlink(DrawerPaths.join(path, ".DS_Store")) == 0 { names = DrawerFS.names(in: path) ?? ["?"] }
        }
        guard names.isEmpty else { return .kept(DrawerSkipReason.stillHolds) }
        return rmdir(path) == 0 ? .removed : .kept(DrawerSkipReason.stillHolds)
    }

    // MARK: Recovery

    /// Closes the entries a crash left open (any move still `written: false`). A pending move counts
    /// as written when its file is no longer at the old place under the recorded inode and an entry
    /// with that inode sits at its destination, or at the numbered name a clash would have given
    /// it (D7). A move with no recorded inode, or with no such file, is dropped as not having
    /// happened, and the file is left alone. The journal is saved with the entries closed; the
    /// returned entries are the ones to offer Undo for.
    @discardableResult
    func recover() -> [JournalEntry] {
        var journal = store.load()
        let opens = journal.openEntries
        guard !opens.isEmpty else { return [] }
        var recovered: [JournalEntry] = []
        for open in opens {
            var entry = open
            entry.moves = open.moves.compactMap { m in m.isWritten ? m : landedMove(m) }
            if entry.moves.isEmpty {
                journal = journal.without(open.id)
            } else {
                journal = journal.updating(entry)
                recovered.append(entry)
            }
        }
        try? store.save(journal)
        return recovered
    }

    /// A pending move as written, if its file really moved; nil if it did not.
    private func landedMove(_ m: JournalMove) -> JournalMove? {
        guard let ino = m.inode else { return nil }
        if let there = DrawerFS.lstat(m.from), DrawerFS.inode(there) == ino { return nil }
        let dir = DrawerPaths.parent(m.to)
        var found: (path: String, st: stat)?
        // Never stops early: free() only enumerates the names a clash could have produced.
        _ = DrawerNames.free(DrawerFS.name(of: m.to), isFolder: m.isFolder) { candidate in
            if found == nil {
                let p = DrawerPaths.join(dir, candidate)
                if let st = DrawerFS.lstat(p), DrawerFS.inode(st) == ino { found = (p, st) }
            }
            return true
        }
        guard let hit = found else { return nil }
        var landed = m
        landed.to = hit.path
        landed.written = .done(modified: m.isFolder ? nil : DrawerFS.modified(hit.st))
        return landed
    }

    // MARK: One move

    private struct MoveRequest {
        var source: String
        var destDir: String
        var destName: String
        /// A rule move must be a plain file that still matches what the review showed.
        var rule: Bool
        var expect: DrawerItemFacts?
    }

    private struct Landing {
        var from: String
        var to: String
        var isFolder: Bool
        var inode: UInt64
        var modified: Date
        var verified: Bool
    }

    private enum Attempt { case moved(Landing), skipped(String) }

    /// Checks a move against the file system as it is now, then makes it. Every path is resolved
    /// again here: the source must be a direct child of the Desktop or an owned drawer and not a
    /// link, the destination folder must still be a real folder in a known place, and a rule move
    /// must still be a plain file with the size and date the review showed.
    private func attempt(_ r: MoveRequest, places: Places) -> Attempt {
        guard let dirReal = DrawerFS.realPath(DrawerPaths.parent(r.source)).flatMap(DrawerPaths.normalize) else {
            return .skipped(DrawerSkipReason.gone)
        }
        guard places.allows(dirReal) else { return .skipped(DrawerSkipReason.outside) }
        let full = DrawerPaths.join(dirReal, DrawerFS.name(of: r.source))
        guard let st = DrawerFS.lstat(full) else { return .skipped(DrawerSkipReason.gone) }
        if DrawerFS.isLink(st) { return .skipped(DrawerSkipReason.link) }
        if places.holdsOwnFolder(full) { return .skipped(DrawerSkipReason.holdsDrawer) }
        if DrawerFacts.isDataless(st) { return .skipped(DrawerSkipReason.inICloud) }
        if r.rule {
            guard DrawerFS.isRegular(st) else { return .skipped(DrawerSkipReason.notPlain) }
            guard let e = r.expect else { return .skipped(DrawerSkipReason.noReview) }
            guard e.size == Int64(st.st_size), abs(e.modified.timeIntervalSince(DrawerFS.modified(st))) <= DrawerUndo.dateTolerance else {
                return .skipped(DrawerSkipReason.changed)
            }
        }
        guard places.allows(r.destDir), isRealFolder(r.destDir) else { return .skipped(DrawerSkipReason.noFolder) }
        if DrawerPaths.key(dirReal) == DrawerPaths.key(r.destDir) { return .skipped(DrawerSkipReason.alreadyThere) }

        // Try the plain name, then Finder-style numbered names. The closure is the rename itself:
        // "taken" means the kernel said EEXIST, and the first name that is not taken is the one
        // that now holds the file, so the loop stops there.
        let isFolder = DrawerFS.isDirectory(st)
        var failure: Int32 = 0
        var landed: String?
        let chosen = DrawerNames.free(r.destName, isFolder: isFolder) { candidate in
            let target = DrawerPaths.join(r.destDir, candidate)
            let code = rename(full, target)
            if code == 0 { landed = target; return false }
            if code == EEXIST { return true }
            failure = code
            return false
        }
        guard let first = landed else {
            return .skipped(chosen == nil && failure == 0 ? DrawerUndo.noFreeName : DrawerSkipReason.failed(failure))
        }

        // Read back where it landed, by name and inode.
        let ino = DrawerFS.inode(st)
        var path = first
        var verified = true
        var landedStat = DrawerFS.lstat(first)
        if landedStat.map({ DrawerFS.inode($0) != ino }) ?? true {
            landedStat = nil
            for name in DrawerFS.names(in: r.destDir) ?? [] {
                let p = DrawerPaths.join(r.destDir, name)
                if let s = DrawerFS.lstat(p), DrawerFS.inode(s) == ino { path = p; landedStat = s; break }
            }
            verified = landedStat != nil
        }
        return .moved(Landing(from: full, to: path, isFolder: isFolder, inode: ino,
                              modified: landedStat.map { DrawerFS.modified($0) } ?? DrawerFS.modified(st), verified: verified))
    }

    // MARK: Helpers

    /// The places a run may touch, resolved. Refuses a root that isn't safe, so Undo and the return
    /// never run against a folder the Desktop sits in.
    private func safePlaces(_ loc: DrawerLocations, _ drawers: [DrawerDef], _ result: inout DrawerRunResult) -> Places? {
        guard let desktop = DrawerFS.resolve(loc.desktop), let root = DrawerFS.resolve(loc.root),
              let home = DrawerFS.resolve(loc.home) else {
            result.refusal = "The Desktop and the Drawers folder must be full paths."
            return nil
        }
        if let why = DrawerPlanValidator.rootRefusal(root: root, desktop: desktop, home: home,
                                                     rootVolume: DrawerFS.volumeID(root), desktopVolume: DrawerFS.volumeID(desktop)) {
            result.refusal = why
            return nil
        }
        return Places.make(desktop: desktop, root: root, drawers: drawers)
    }

    /// Makes the root and a drawer's folder when they are missing (a first sort, or a drawer folder
    /// deleted by hand). Never makes parents, so a typo can't build a tree.
    private func ensureFolder(_ home: String, root: String) -> Bool {
        ensureDirectory(root) && ensureDirectory(home)
    }

    private func ensureDirectory(_ path: String) -> Bool {
        if DrawerFS.lstat(path) == nil, mkdir(path, 0o755) != 0, errno != EEXIST { return false }
        return isRealFolder(path)
    }

    /// A folder, not a link to one, whose real path is the one named.
    private func isRealFolder(_ path: String) -> Bool {
        guard let st = DrawerFS.lstat(path), DrawerFS.isDirectory(st), let real = DrawerFS.realPath(path) else { return false }
        return DrawerPaths.key(real) == DrawerPaths.key(path)
    }
}

/// The real folders a run may reach: the Desktop, the Drawers root and the owned drawers directly
/// in it. Built once per run from resolved paths and compared with what each move resolves to.
private struct Places {
    var desktop: String
    var root: String
    /// Owned drawer id to its real folder, only for ones directly in the root.
    var homes: [String: String]

    var homeKeys: Set<String> { Set(homes.values.map(DrawerPaths.key)) }

    static func make(desktop: String, root: String, drawers: [DrawerDef]) -> Places? {
        guard let d = DrawerPaths.normalize(desktop), let r = DrawerPaths.normalize(root) else { return nil }
        var homes: [String: String] = [:]
        for drawer in drawers where drawer.owned && homes[drawer.id] == nil {
            if let h = DrawerFS.resolve(drawer.home), DrawerPaths.isChild(h, of: r) { homes[drawer.id] = h }
        }
        return Places(desktop: d, root: r, homes: homes)
    }

    /// A folder a file may come from or go to: the Desktop or an owned drawer.
    func allows(_ dir: String) -> Bool {
        let key = DrawerPaths.key(dir)
        return key == DrawerPaths.key(desktop) || homeKeys.contains(key)
    }

    /// The path is, or holds, the root or a drawer's folder.
    func holdsOwnFolder(_ path: String) -> Bool {
        DrawerPaths.isInside(root, path) || homes.values.contains { DrawerPaths.isInside($0, path) }
    }
}
