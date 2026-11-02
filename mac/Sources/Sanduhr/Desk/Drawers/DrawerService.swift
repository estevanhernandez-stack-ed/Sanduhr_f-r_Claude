import Foundation
import Observation

/// One thing in a drawer's folder, as the watcher lists it. Names stay in memory for the panels;
/// nothing here is logged or written to state.
struct DrawerEntry: Equatable, Sendable {
    var name: String
    var path: String
    var isFolder: Bool
}

/// A sort plan waiting for the user's review. Nothing has moved.
struct DrawerReview: Equatable, Sendable {
    var plan: DrawerPlan
    /// What the review showed for each source, which Apply checks again.
    var planned: [String: DrawerItemFacts]
    var fileCount: Int { plan.moves.count }
    var drawerCount: Int { plan.drawers.count }
}

/// What Reset Sanduhr… did about the drawers, with counts only.
struct DrawerResetReport: Equatable, Sendable {
    var returned = 0
    var stayedBehind = 0
    var prefsRemoved = true
    var journalRemoved = true

    /// A line for the Reset flow, or nil when nothing stayed.
    var line: String? {
        var parts: [String] = []
        if stayedBehind > 0 {
            parts.append(stayedBehind == 1 ? "1 file stayed in the Drawers folder on your Desktop."
                                           : "\(stayedBehind) files stayed in the Drawers folder on your Desktop.")
        }
        if !prefsRemoved { parts.append("The drawer settings could not be removed.") }
        if !journalRemoved { parts.append("The drawer journal could not be removed.") }
        return parts.isEmpty ? nil : parts.joined(separator: " ")
    }
}

/// Where the Desktop is. The production default is the system's Desktop path (asking for the path
/// reads nothing). A debug build also honors `SANDUHR_DRAWERS_DESKTOP` for the smoke scenario; a
/// release build ignores it.
enum DrawerDesktop {
    static let overrideVariable = "SANDUHR_DRAWERS_DESKTOP"

    static func standard(environment: [String: String] = ProcessInfo.processInfo.environment) -> String {
        #if DEBUG
        if let p = environment[overrideVariable], DrawerPaths.normalize(p) != nil { return p }
        #endif
        return FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Desktop"
    }
}

/// How a drawer's folder is listed: folders first, then names in Finder order, hidden items and
/// `.DS_Store` left out.
enum DrawerListing {
    static func read(_ home: String) -> [DrawerEntry] {
        let entries = (DrawerFS.names(in: home) ?? []).compactMap { name -> DrawerEntry? in
            guard !name.hasPrefix(".") else { return nil }
            let path = DrawerPaths.join(home, name)
            guard let st = DrawerFS.lstat(path) else { return nil }
            if (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.isHiddenKey]))?.isHidden == true { return nil }
            return DrawerEntry(name: name, path: path, isFolder: DrawerFS.isDirectory(st))
        }
        return sorted(entries)
    }

    static func sorted(_ entries: [DrawerEntry]) -> [DrawerEntry] {
        entries.sorted { a, b in
            if a.isFolder != b.isFolder { return a.isFolder }
            return a.name.localizedStandardCompare(b.name) == .orderedAscending
        }
    }
}

/// Drawers' live side (docs/mac-drawers-setup-spec.md, sections 1, 3, 5 and 7): the settings, the
/// pending review, Undo, hand moves, renames, turning on and off, and one FSEvents stream over the
/// drawer folders. Every location comes in through the initializer (the Desktop, the home folder,
/// the journal, the defaults suite), so a test points them all at a temp folder. Building the
/// service reads preferences and nothing else: the Desktop is read only when the user sorts, and
/// only the drawer folders are read after that.
@MainActor
@Observable
final class DrawerService {
    static let keepRuns = 10

    private(set) var settings: DrawerSettings
    /// The sort waiting for Apply, Edit or Dismiss.
    private(set) var pending: DrawerReview?
    /// What the last run did, for the result bar.
    private(set) var lastResult: DrawerRunResult?
    /// The last 10 runs Undo can reverse, newest first.
    private(set) var runs: [JournalEntry] = []
    /// Runs a crash left open, closed at launch and offered as Undo.
    private(set) var recovered: [JournalEntry] = []
    /// Each drawer's contents by id, kept current by the watcher.
    private(set) var contents: [String: [DrawerEntry]] = [:]

    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let desktop: String
    @ObservationIgnored private let home: String
    @ObservationIgnored private let store: DrawerJournalStore
    @ObservationIgnored private let mover: DrawerMover
    @ObservationIgnored private let turnDeskOn: () -> Void
    @ObservationIgnored private let readFacts: (String) -> [DrawerItemFacts]
    @ObservationIgnored private let watchQueue: DispatchQueue
    @ObservationIgnored private var watcher: DrawerWatcher?
    @ObservationIgnored private var watched: [(key: String, id: String)] = []
    @ObservationIgnored private var stoppedForReset = false

    init(defaults: UserDefaults, desktop: String, home: String, store: DrawerJournalStore,
         turnDeskOn: @escaping () -> Void,
         readFacts: @escaping (String) -> [DrawerItemFacts] = DrawerFacts.list,
         mover: DrawerMover? = nil,
         watchQueue: DispatchQueue = DispatchQueue(label: "sanduhr.drawers.watch")) {
        self.defaults = defaults
        self.desktop = desktop
        self.home = home
        self.store = store
        self.mover = mover ?? DrawerMover(store: store)
        self.turnDeskOn = turnDeskOn
        self.readFacts = readFacts
        self.watchQueue = watchQueue
        self.settings = DrawerPrefs.load(from: defaults)
    }

    /// The app's one service, over the real defaults suite, journal and Desktop.
    static let shared = DrawerService(
        defaults: .desk, desktop: DrawerDesktop.standard(), home: NSHomeDirectory(), store: .standard(),
        turnDeskOn: {
            UserDefaults.desk.set(true, forKey: DeskController.enabledKey)
            DeskController.shared.apply()
        })

    // MARK: Locations

    var effectiveRoot: String { settings.root ?? DrawerPaths.join(desktop, "Drawers") }

    /// The root is Sanduhr's own `<Desktop>/Drawers`, which turning off may remove once empty.
    var rootIsDefault: Bool {
        guard let r = settings.root else { return true }
        guard let a = DrawerPaths.normalize(r), let b = DrawerPaths.normalize(DrawerPaths.join(desktop, "Drawers")) else { return false }
        return DrawerPaths.key(a) == DrawerPaths.key(b)
    }

    private var loc: DrawerLocations { DrawerLocations(desktop: desktop, root: effectiveRoot, home: home) }
    var hasOwnedDrawer: Bool { settings.drawers.contains { $0.owned } }

    // MARK: Launch

    /// Called once at launch. With Drawers off it does nothing at all. With it on, it closes any run
    /// a crash left open (offering Undo for what moved), loads the run list and starts the watcher.
    /// It never reads the Desktop.
    func launch() {
        guard settings.enabled, !stoppedForReset else { return }
        recovered = mover.recover()
        refreshRuns()
        restartWatcher()
    }

    // MARK: On and off

    /// Turns Drawers on, and the Desk with it. No file is read or moved until the first sort.
    func turnOn() {
        guard !stoppedForReset else { return }
        update { $0.enabled = true }
        turnDeskOn()
        refreshRuns()
        restartWatcher()
    }

    /// How many files turning off would put back, for "Put N files back on the Desktop?".
    func offCount() -> Int {
        returnPlan().moves.count
    }

    /// The confirmation's question.
    func offPrompt() -> String { DrawerReturn.prompt(offCount()) }

    /// The confirmed off: puts every file in every owned drawer back on the Desktop, removes the
    /// folders that emptied, keeps the rules, and leaves a drawer that still holds something listed.
    @discardableResult
    func confirmTurnOff() -> DrawerRunResult {
        let result = putEverythingBack()
        pending = nil
        update { $0.enabled = false }
        stopWatcher()
        contents = [:]
        lastResult = result
        return result
    }

    // MARK: Root and drawers

    /// Chooses the Drawers root: allowed only while no owned drawer exists, and only a safe one.
    /// Nil puts the default back. Returns why it was refused, or nil.
    @discardableResult
    func chooseRoot(_ path: String?) -> String? {
        if hasOwnedDrawer { return "The Drawers folder can't change while drawers exist. Turn Drawers off first." }
        guard let path else { update { $0.root = nil }; return nil }
        guard let root = DrawerFS.resolve(path), let desk = DrawerFS.resolve(desktop), let h = DrawerFS.resolve(home) else {
            return "The Drawers folder must be a full path."
        }
        if let why = DrawerPlanValidator.rootRefusal(root: root, desktop: desk, home: h, rootVolume: DrawerFS.volumeID(root),
                                                     desktopVolume: DrawerFS.volumeID(desk)) { return why }
        update { $0.root = root }
        return nil
    }

    /// Adds a folder to look at. Its contents are never moved by Sanduhr. Returns why it was
    /// refused, or nil.
    @discardableResult
    func addViewDrawer(folder: String, name: String? = nil) -> String? {
        let label = name ?? DrawerFS.name(of: folder)
        guard DrawerNames.isValid(label) else { return "\"\(label)\" can't be a drawer name." }
        guard settings.drawers.count < DrawerPlanValidator.maxDrawers else { return "At most \(DrawerPlanValidator.maxDrawers) drawers." }
        guard let real = DrawerFS.realPath(folder), let st = DrawerFS.lstat(real), DrawerFS.isDirectory(st) else {
            return "That isn't a folder."
        }
        guard let desk = DrawerFS.resolve(desktop), let root = DrawerFS.resolve(effectiveRoot) else { return "The Desktop isn't a full path." }
        if DrawerPaths.isInside(desk, real) || DrawerPaths.isVolumeRoot(real) { return "That folder holds the Desktop." }
        if DrawerPaths.isInside(real, root) || DrawerPaths.isInside(root, real) { return "That folder is part of the Drawers folder." }
        if settings.drawers.contains(where: { DrawerPaths.normalize($0.home).map(DrawerPaths.key) == DrawerPaths.key(real) }) {
            return "That folder is already a drawer."
        }
        if settings.drawers.contains(where: { DrawerPaths.key($0.name) == DrawerPaths.key(label) }) { return "A drawer named \"\(label)\" exists." }
        var id = DrawerIds.from(label), n = 2
        while settings.drawers.contains(where: { $0.id == id }) { id = String(DrawerIds.from(label).prefix(DrawerIds.maxLength - 3)) + "-\(n)"; n += 1 }
        update { $0.drawers.append(DrawerDef(id: id, name: label, home: real, owned: false)) }
        restartWatcher()
        return nil
    }

    /// Removes a view drawer from the list. An owned drawer is not removed here: turning Drawers off does that.
    @discardableResult
    func removeViewDrawer(_ id: String) -> Bool {
        guard let d = settings.drawers.first(where: { $0.id == id }), !d.owned else { return false }
        update { s in
            s.drawers.removeAll { $0.id == id }
            s.looks[id] = nil
            s.places[id] = nil
            s.folded.removeAll { $0 == id }
            s.hidden.removeAll { $0 == id }
        }
        contents[id] = nil
        restartWatcher()
        return true
    }

    // MARK: Sort, review, apply

    /// Reads the Desktop's top level and holds the rules' plan as the pending review. Nil when
    /// Drawers is off or nothing matches. This is the only place the Desktop is listed.
    @discardableResult
    func sort(now: Date = Date()) -> DrawerReview? {
        guard settings.enabled, !stoppedForReset else { return nil }
        let facts = readFacts(desktop)
        let plan = DrawerRules.plan(facts: facts, rules: settings.rules, drawers: settings.drawers,
                                    desktop: desktop, root: effectiveRoot, now: now)
        guard !plan.moves.isEmpty else { pending = nil; return nil }
        let planned = Dictionary(facts.map { (DrawerPaths.join(desktop, $0.name), $0) }, uniquingKeysWith: { a, _ in a })
        pending = DrawerReview(plan: plan, planned: planned)
        return pending
    }

    func dismissReview() { pending = nil }

    /// The review after the user's edits (Edit unticks files and renames new drawers). Apply
    /// still checks it again, against the facts the review showed.
    func replacePending(_ plan: DrawerPlan) {
        guard let p = pending else { return }
        pending = DrawerReview(plan: plan, planned: p.planned)
    }

    /// Applies the pending review. The mover checks it again, so what changed since is dropped and named.
    @discardableResult
    func apply() -> DrawerRunResult? {
        guard let review = pending else { return nil }
        pending = nil
        return run(review.plan, planned: review.planned)
    }

    /// A file dropped on a drawer: moves at once, journaled as the user's.
    @discardableResult
    func moveByHand(_ source: String, to drawerId: String) -> DrawerRunResult {
        run(DrawerPlan(source: .user, drawers: [], moves: [DrawerMove(source: source, drawerId: drawerId)]), planned: [:])
    }

    private func run(_ plan: DrawerPlan, planned: [String: DrawerItemFacts]) -> DrawerRunResult {
        let before = settings.drawers
        // New drawers are saved before the first move, with their real paths, so a crash mid-run
        // still leaves Undo and the return knowing where the files went.
        let added = plan.drawers.filter { d in !before.contains { $0.id == d.id } }
            .map { d -> DrawerDef in var r = d; r.home = DrawerFS.resolve(d.home) ?? d.home; return r }
        if !added.isEmpty { update { $0.drawers.append(contentsOf: added) } }
        let result = mover.apply(plan, at: loc, existing: before, planned: planned)
        // A drawer whose folder never got made (nothing moved into it) is not kept.
        let unused = Set(added.filter { !DrawerFS.exists($0.home) }.map(\.id))
        if !unused.isEmpty { update { $0.drawers.removeAll { unused.contains($0.id) } } }
        lastResult = result
        refreshRuns()
        restartWatcher()
        return result
    }

    // MARK: Items in a drawer

    /// Renames one item inside a drawer's folder with the exclusive rename, so it never replaces
    /// something there. Returns why it was refused, or nil.
    @discardableResult
    func renameItem(_ path: String, to newName: String) -> String? {
        guard let found = DrawerItemGuard.entry(path, drawers: settings.drawers) else { return "That item isn't in a drawer." }
        let name = newName.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty, name != ".", name != "..", !name.contains("/"), !name.contains(":") else {
            return "\"\(newName)\" can't be a file name."
        }
        let dest = DrawerPaths.join(DrawerPaths.parent(found.path), name)
        if DrawerFS.exists(dest) { return "Something named \"\(name)\" is already there." }
        return mover.rename(found.path, dest) == 0 ? nil : "It couldn't be renamed."
    }

    /// Moves an item to the Trash, from the user's own command only (`DrawerTrash`).
    @discardableResult
    func trashItem(_ path: String) -> String? { DrawerTrash.trash(path, drawers: settings.drawers) }

    // MARK: Undo


    /// Reverses one run, by its journal id.
    @discardableResult
    func undo(_ entryId: String) -> DrawerRunResult? {
        guard let entry = store.load().entries.first(where: { $0.id == entryId }) else { return nil }
        let result = mover.undo(DrawerJournal.recovered(entry), at: loc, drawers: settings.drawers)
        recovered.removeAll { $0.id == entryId }
        lastResult = result
        refreshRuns()
        refreshContents()
        return result
    }

    private func refreshRuns() {
        runs = Array(store.load().entries.filter { !$0.writtenMoves.isEmpty }
            .sorted { $0.at > $1.at }.prefix(Self.keepRuns))
    }

    // MARK: Rename

    /// Renames a drawer. An owned drawer's folder is renamed with the exclusive rename (never over
    /// something there) and the journal's paths follow, so Undo still works. A view drawer only
    /// changes its label. Returns why it was refused, or nil.
    @discardableResult
    func rename(_ id: String, to newName: String) -> String? {
        guard let d = settings.drawers.first(where: { $0.id == id }) else { return "There is no such drawer." }
        guard DrawerNames.isValid(newName) else { return "\"\(newName)\" can't be a folder name." }
        if DrawerPaths.key(newName) == DrawerPaths.key(d.name) { return "That is already its name." }
        if settings.drawers.contains(where: { $0.id != id && DrawerPaths.key($0.name) == DrawerPaths.key(newName) }) {
            return "A drawer named \"\(newName)\" exists."
        }
        guard d.owned else {
            update { s in if let i = s.drawers.firstIndex(where: { $0.id == id }) { s.drawers[i].name = newName } }
            return nil
        }
        guard let old = DrawerPaths.normalize(d.home), let root = DrawerFS.resolve(effectiveRoot) else { return "The drawer's folder isn't a full path." }
        let new = DrawerPaths.join(DrawerPaths.parent(old), newName)
        if let st = DrawerFS.lstat(old) {
            guard DrawerFS.isDirectory(st), let realOld = DrawerFS.realPath(old),
                  DrawerPaths.isChild(realOld, of: root) else { return "The drawer's folder isn't in the Drawers folder." }
            let realNew = DrawerPaths.join(DrawerPaths.parent(realOld), newName)
            if DrawerFS.exists(realNew) { return "A folder named \"\(newName)\" is already there." }
            let code = mover.rename(realOld, realNew)
            guard code == 0 else { return "The folder couldn't be renamed." }
            // The journal holds real paths from the mover; the settings may hold the path as given.
            var journal = store.load().repointing(from: realOld, to: realNew)
            if DrawerPaths.key(old) != DrawerPaths.key(realOld) { journal = journal.repointing(from: old, to: new) }
            try? store.save(journal)
        }
        update { s in
            if let i = s.drawers.firstIndex(where: { $0.id == id }) { s.drawers[i].name = newName; s.drawers[i].home = new }
        }
        refreshRuns()
        restartWatcher()
        return nil
    }

    // MARK: Reset

    /// Reset Sanduhr…: the return first, then the `drawers.*` preferences and the journal. Reports
    /// counts only. After this the service writes nothing, so a late save can't put settings back.
    func resetAll() -> DrawerResetReport {
        var report = DrawerResetReport()
        if hasOwnedDrawer {
            let result = putEverythingBack()
            report.returned = result.moved.count
            report.stayedBehind = stayedBehind()
        }
        stopWatcher()
        stoppedForReset = true
        pending = nil
        contents = [:]
        runs = []
        recovered = []
        report.prefsRemoved = DrawerPrefs.clear(defaults)
        let fm = FileManager.default
        for url in [store.url, store.url.deletingLastPathComponent().appendingPathComponent(store.url.lastPathComponent + ".tmp")] {
            if fm.fileExists(atPath: url.path) { try? fm.removeItem(at: url) }
            if fm.fileExists(atPath: url.path) { report.journalRemoved = false }
        }
        settings = DrawerSettings()
        return report
    }

    // MARK: The return

    private func returnPlan() -> ReturnPlan {
        guard let places = DrawerFS.resolve(desktop).flatMap({ d in DrawerFS.resolve(effectiveRoot).map { (d, $0) } }) else {
            return ReturnPlan(moves: [], foldersToRemove: [])
        }
        return DrawerReturn.plan(drawers: settings.drawers.filter(\.owned), root: places.1, desktop: places.0,
                                 childrenOf: DrawerMover.children, exists: DrawerFS.exists, removeRoot: rootIsDefault)
    }

    private func putEverythingBack() -> DrawerRunResult {
        guard hasOwnedDrawer else { return DrawerRunResult() }
        let result = mover.returnAll(drawers: settings.drawers, at: loc, removeRoot: rootIsDefault)
        // An owned drawer whose folder is gone leaves the list; one that kept something stays listed.
        update { s in s.drawers.removeAll { $0.owned && !DrawerFS.exists($0.home) } }
        return result
    }

    private func stayedBehind() -> Int {
        settings.drawers.filter(\.owned).reduce(0) { $0 + DrawerMover.children($1.home).count }
    }

    // MARK: Watching

    private func restartWatcher() {
        stopWatcher()
        guard settings.enabled, !stoppedForReset else { return }
        var entries: [(key: String, id: String)] = []
        var paths: [String] = []
        for d in settings.drawers {
            guard let real = DrawerFS.realPath(d.home), let st = DrawerFS.lstat(real), DrawerFS.isDirectory(st) else { continue }
            paths.append(real)
            entries.append((DrawerPaths.key(real), d.id))
        }
        refreshContents()
        guard !paths.isEmpty else { return }
        watched = entries
        let w = DrawerWatcher(paths: paths, queue: watchQueue) { [weak self] changed in
            Task { @MainActor [weak self] in self?.changed(changed) }
        }
        if w.start() { watcher = w }
    }

    private func stopWatcher() {
        watcher?.stop()
        watcher = nil
        watched = []
    }

    /// A change under some drawer folders: only those drawers are listed again.
    private func changed(_ paths: [String]) {
        guard watcher != nil else { return }
        var ids = Set<String>()
        for p in paths {
            let key = DrawerPaths.key(DrawerPaths.normalize(p) ?? p)
            for w in watched where key == w.key || key.hasPrefix(w.key + "/") { ids.insert(w.id) }
        }
        refreshContents(ids)
    }

    private func refreshContents(_ only: Set<String>? = nil) {
        for d in settings.drawers where only?.contains(d.id) ?? true {
            contents[d.id] = DrawerListing.read(DrawerFS.realPath(d.home) ?? d.home)
        }
        let known = Set(settings.drawers.map(\.id))
        for id in contents.keys where !known.contains(id) { contents[id] = nil }
    }

    // MARK: Settings

    private func update(_ change: (inout DrawerSettings) -> Void) {
        change(&settings)
        guard !stoppedForReset else { return }
        DrawerPrefs.save(settings, to: defaults)
    }

    /// Look, opacity, folds, places and the Tidy corner are stored values the panels (item 5) change.
    func updateSettings(_ change: (inout DrawerSettings) -> Void) { update(change) }
}
