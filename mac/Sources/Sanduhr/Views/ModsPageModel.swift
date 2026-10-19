import AppKit
import SwiftUI

// The Mods page's model: ModsPageModel's stored state, reads, Catalog, More and Compare. Split
// out of ModsSettings.swift on 2026-10-19; By Seat and the own mod's actions are in
// ModsPageModel+BySeat.swift.

/// The Mods page's state (item 64): every Claude Code folder's mods and plugins, the `claude`
/// found for Check, each Check's answer, and (slice 2) Sanduhr's own mod per folder with its
/// switch, Update and Remove. Reads and writes run off the main thread; the only writes are the
/// own mod's, by receipt (`ModSwitch`). Kept by SettingsNavigation, so the summary card and
/// state.yaml read the same model while the window lives.
@MainActor
@Observable
final class ModsPageModel {
    private(set) var inventory: [ModFolderInventory] = []
    private(set) var loaded = false
    private(set) var loading = false
    /// The `claude` Check runs, nil when none was found (known once `loaded`).
    private(set) var claude: String?
    /// Each Check's answer by item id, and the ones running.
    var checks: [String: ModCheckOutcome] = [:]
    var checking: Set<String> = []

    /// Sanduhr's own mod in each folder (set by `load`; tests seed it), the app's version of it,
    /// and what the last action said.
    var own: [OwnModRow] = []
    private(set) var appMod: AppModVersion?
    var ownNotes: [String: OwnModNote] = [:]
    var ownBusy: Set<String> = []
    /// An update that can do more than the version in use, waiting for an answer.
    var updateQuestion: OwnModUpdateQuestion?

    let home = NSHomeDirectory()
    var installer = IntegrationInstaller.standard

    /// `moreStore` keeps the More disclosure's choice (`moreExpandedKey`); tests give one in memory.
    init(moreStore: DefaultsStore = UserDefaults.standard) {
        self.moreStore = moreStore
        moreChoice = moreStore.object(forKey: Self.moreExpandedKey) as? Bool
    }

    var counts: ModCounts { ModCounts(inventory) }

    /// Per folder, the line naming both `.claude.json` files when the default folder has two
    /// (item 71, slice 0).
    private(set) var configNotes: [String: String] = [:]
    /// Receipts Sanduhr couldn't read and set aside (item 71, slice 0): their entries can't be
    /// undone from here, the others still can.
    private(set) var receiptsSkipped = 0
    var addNote: String?
    /// The folders added by hand (Add Folder…), for their Forget button.
    private(set) var addedFolders: [String] = []

    /// Where Sanduhr keeps its seat preferences (Add Folder…, later labels and marks).
    var store: DefaultsStore { KeychainStore.accounts.defaults }

    /// The folders to list: found ones (never a `*.config-backup-*` or `.claude-backup-*` copy),
    /// the ones added by hand, the accounts' linked ones, and ones Sanduhr installed into.
    nonisolated static func folders(home: String, environment: [String: String], added: [String] = [],
                                    linked: [String], installed: [String]) -> [String] {
        var paths = ClaudeCodeFolders.discover(home: home, environment: environment).map(\.path)
        // A folder added by hand is listed while it still looks like a Claude Code folder.
        let stillHomes = added.filter { ClaudeCodeFolders.looksLikeHome($0, home: home) }
        for p in stillHomes + linked + installed {
            let n = AccountData.normalized(p)
            if !paths.contains(n) { paths.append(n) }
        }
        return paths
    }

    /// The line for a folder whose `.claude.json` sits in both places, nil otherwise: which one
    /// Claude Code uses and when it reads the other. Whether each is signed in is all it says.
    nonisolated static func configNote(_ candidates: [ClaudeCodeFolders.ConfigCandidate],
                                       display: (String) -> String) -> String? {
        let present = candidates.filter(\.exists)
        guard present.count > 1, let used = present.first(where: \.inUse),
              let other = present.first(where: { !$0.inUse }) else { return nil }
        func state(_ c: ClaudeCodeFolders.ConfigCandidate) -> String { c.signedIn ? "signed in" : "not signed in" }
        let when = other.path.hasSuffix("/\(ClaudeCodeFolders.defaultName)/\(ClaudeCodeFolders.configName)")
            ? "only when CLAUDE_CONFIG_DIR names this folder" : "when CLAUDE_CONFIG_DIR is not set"
        return "Two .claude.json files: \(display(used.path)) (\(state(used))) is the one Sanduhr reads and installs into. "
            + "\(display(other.path)) (\(state(other))) is what Claude Code reads \(when). Remove always edits the file Sanduhr wrote to."
    }

    /// How Sanduhr knows each folder `folders` lists, first reason first.
    nonisolated static func origins(home: String, environment: [String: String], added: [String],
                                    linked: [String], installed: [String]) -> [String: Seat.Origin] {
        var out: [String: Seat.Origin] = [:]
        func note(_ paths: [String], _ o: Seat.Origin) {
            for p in paths.map(AccountData.normalized) where out[p] == nil { out[p] = o }
        }
        note(ClaudeCodeFolders.discover(home: home).map(\.path), .found)
        note(ClaudeCodeFolders.discover(home: home, environment: environment).map(\.path), .environment)
        note(added, .added)
        note(linked, .linked)
        note(installed, .installed)
        return out
    }

    /// Two passes: first what Sanduhr's own mod needs (the folders, its rows, `claude`), so
    /// Meters above the prompt never waits on the Catalog; then every seat's scan.
    /// The accounts' linked folders the last read used: Turn On and Off read again with them.
    private(set) var lastLinked: [String] = []

    func load(linked: [String]) async {
        lastLinked = linked
        guard !loading else { return }
        loading = true
        defer { loading = false }
        let home = self.home
        let installer = self.installer
        let keptStore = self.keptStore
        let added = SeatPreferences.added(in: store)
        let first = await Task.detached(priority: .userInitiated) { () -> ModsPageRead in
            let env = ProcessInfo.processInfo.environment
            let installed = installer.installedFolders()
            let paths = ModsPageModel.folders(home: home, environment: env, added: added, linked: linked, installed: installed)
            let app = AppModVersion.read(installer.scripts)
            var notes: [String: String] = [:]
            var configs: [String: [ClaudeCodeFolders.ConfigCandidate]] = [:]
            for p in paths {
                let display = { (s: String) in ClaudeCodeFolders.Folder(path: s).display(home: home) }
                configs[p] = ClaudeCodeFolders.configCandidates(for: p, home: home)
                notes[p] = ModsPageModel.configNote(configs[p] ?? [], display: display)
            }
            let claude = ModCheck.findClaude(environment: env, home: home)
            var read = ModsPageRead(paths: paths, scans: [], inventory: [],
                                    claude: claude,
                                    own: paths.map { OwnModRow.read(installer, folder: $0, app: app) }, app: app,
                                    notes: notes, configs: configs, skipped: installer.readReceipts().skipped,
                                    origins: ModsPageModel.origins(home: home, environment: env, added: added, linked: linked,
                                                                   installed: installed))
            read.version = ModsPageModel.claudeVersion(claude)
            read.environmentSeat = env["CLAUDE_CONFIG_DIR"].flatMap { $0.isEmpty ? nil : ClaudeProcesses.seat(configDir: $0, home: home) }
            // What is kept decides whether More starts open; New Mod reads it again with its states.
            read.kept = keptStore.list()
            return read
        }.value
        claude = first.claude
        own = first.own
        kept = first.kept
        appMod = first.app
        configNotes = first.notes
        addedFolders = added.map(AccountData.normalized)
        receiptsSkipped = first.skipped
        seatConfigs = first.configs
        seatOrigins = first.origins
        claudeVersion = first.version
        environmentSeat = first.environmentSeat
        ownLoaded = true
        // One read per seat (item 71, slice 1): its plugins are the inventory's, and its
        // `.claude.json` placement is the one just read. Then By Seat's sections (slice 1b),
        // and which seats a running claude uses.
        let paths = first.paths, configs = first.configs
        let second = await Task.detached(priority: .userInitiated) { () -> ModsPageRead in
            let scans = paths.map { SeatScanner.scan(seat: $0, home: home, config: configs[$0]) }
            let defaultSeat = (home as NSString).appendingPathComponent(ClaudeCodeFolders.defaultName)
            let running = ClaudeProcesses.running(home: home)
            var read = ModsPageRead(paths: paths, scans: scans, inventory: [], claude: nil, own: [], app: nil,
                                    notes: [:], configs: configs, skipped: 0)
            read.reports = SeatReports.build(scans.map { SeatReports.Input(seat: $0.seat, isDefault: $0.seat == defaultSeat, scan: $0) },
                                             home: home, configs: configs)
            read.running = Set(running.sessions.map(\.seat))
            read.unreadableProcesses = running.unreadable
            return read
        }.value
        scans = second.scans
        inventory = scans.map { ModFolderInventory(folder: $0.seat, items: $0.plugins) }
        reports = second.reports
        running = second.running
        unreadableProcesses = second.unreadableProcesses
        refreshSeats()
        let ids = Set(inventory.flatMap(\.items).map(\.id))
        checks = checks.filter { ids.contains($0.key) }
        loaded = true
    }

    /// The seats with their current labels and marks, and the Catalog built from them. Reads
    /// Sanduhr's preferences only; the folders were read by `load`.
    func refreshSeats() {
        let store = self.store, home = self.home
        seats = scans.map { scan in
            Seats.seat(scan.seat, origin: seatOrigins[scan.seat] ?? .found, home: home, store: store,
                       config: seatConfigs[scan.seat] ?? [])
        }
        catalog = ConfigCatalog.build(Array(zip(seats, scans)).map { (seat: $0.0, scan: $0.1) })
        loads = scans.map(SessionLoad.build)
        if let s = filter.seat, !seats.contains(where: { $0.path == s }) { filter.seat = nil }
        applyPendingSeat()
    }

    /// The Settings window closed: Mods & Config reopens on its Catalog, nothing focused, and
    /// More as the user left it (a link's opening of it was for the one window).
    func windowClosed() {
        tab = .catalog
        focus = nil
        moreOpenedByLink = false
        clearConfigNotes()
    }

    /// Both notes gone: the picker moved to another folder, or the window closed.
    func clearConfigNotes() {
        exportNotes.clearNotes()
        importNotes.clearNotes()
    }

    /// Where an import's note sits: under `folder`'s header when it is a listed seat, else
    /// above the picker (an import by Other Folder… has no header to sit under).
    func importNoteFolder(_ folder: String) -> String? {
        let p = AccountData.normalized(folder)
        return seats.contains { $0.path == p } ? p : nil
    }

    /// Seats with files Sanduhr couldn't read, for the Catalog's note.
    func unreadableNotes(demo: Bool) -> [String] {
        ConfigCatalog.unreadableNotes(Array(zip(seats, scans)).map { (seat: $0.0, scan: $0.1) }, demo: demo)
    }

    /// The seats the Catalog's Seat filter offers: no work seat in demo mode.
    func filterSeats(demo: Bool) -> [Seat] { seats.filter { !(demo && $0.isWork) } }

    /// Sanduhr's own mod rows are read (the first pass of `load`).
    private(set) var ownLoaded = false
    private var seatOrigins: [String: Seat.Origin] = [:]

    // MARK: Catalog | By Seat (item 71, slice 1)

    /// Which tab shows, kept while the Settings window is open (closing it goes back to Catalog).
    var tab: ModsTab = .catalog
    var filter = CatalogFilter()
    /// A chip clicked on the Catalog: By Seat opens at this seat, with this card's items lit.
    var focus: ModsFocus?
    private(set) var seats: [Seat] = []
    private(set) var scans: [SeatScan] = []
    private(set) var catalog: [CatalogCard] = []
    /// Each seat's session load, in seat order (slice 1c).
    private(set) var loads: [SessionLoad] = []
    /// The Catalog's open groups and the plugin cards showing what's inside, kept while the
    /// window lives; plugins sorted by name or by session load.
    var openGroups: Set<CatalogGroup> = Set(CatalogGroup.allCases.filter(\.startsOpen))
    var openContents: Set<String> = []
    /// Runs inside a group folded by their title (2026-10-14), "group/title", kept while the
    /// window lives; every run starts open.
    var closedRuns: Set<String> = []
    /// The cards opened from their compact line (item 75, slice A), kept while the window lives.
    var openCards: Set<String> = []
    var sortByLoad = false
    /// The Mod Studio sheet is up (item 75, slice B), and the tab it opens on (slice C).
    var studioOpen = false
    var studioTab: StudioTab = .look
    /// New Mod, the Build with Claude prompt (slice E), and the tab it opens on.
    var newModOpen = false
    /// Export Config (item 76, slice C): the sheet's rows while it's open.
    var configExport: ConfigExportState?
    /// Open Config Pack (item 76, slice D): the sheet while open, and imports not undone.
    var configImport: ConfigImportState?
    /// What the last export and the last import said, per folder (polish 2026-10-17, #8): a
    /// folder's header shows its own line only. A refusal with no folder (a pack from Finder
    /// that isn't one Sanduhr opens) sits in the general slot, above the picker. Both clear
    /// when the picker changes folder, when the window closes, and when that folder's next
    /// export or import starts.
    var exportNotes = FolderNotes()
    var importNotes = FolderNotes()
    var imports: [ConfigImport.Record] = ConfigImport.Log.standard.list()
    /// Imports whose Undo is running now.
    var undoingImports: Set<String> = []
    var newModWay: NewModWay = .build
    /// Check's answer for each proposed mod (slice G), by proposal id.
    var proposalChecks: [String: ModCheckOutcome] = [:]
    /// Mods made in sessions, and the ones Sanduhr kept (slice F), with each kept one's Check.
    var sessionMods: [SessionModRow] = []
    var kept: [KeptMod] = []
    var keptChecks: [String: ModCheckOutcome] = [:]
    var keptBusy: Set<String> = []
    /// The kept mod whose pack Export… is writing (polish 2026-10-17, #22), nil between exports.
    var exportingKept: String?
    /// What From Claude's last action said (polish 2026-10-17, #9): orange only when it failed.
    /// Cleared when New Mod closes, so each open starts clean.
    var keptNote: KeptNote?
    /// Publish to a marketplace of your own (item 76, slice F): the confirm while open, and the
    /// commands that add the last one published on another machine.
    var publishing: MarketplacePublish?
    var publishedCommands: String?
    var keptStore = KeptMods.standard
    /// Each kept mod's state per folder (slice F2), by mod id then folder.
    var keptStates: [String: [String: KeptModState]] = [:]
    /// The About tab's Check of the mod this Sanduhr carries, and whether it is running.
    private(set) var ownCheck: ModCheckOutcome?
    private(set) var checkingOwn = false
    private var seatConfigs: [String: [ClaudeCodeFolders.ConfigCandidate]] = [:]

    /// Card ids whose Check failed or found a high risk: Needs a look.
    var failedChecks: Set<String> {
        Set(catalog.filter { card in
            card.itemIDs.contains { id in
                guard case .report(let r)? = checks[id] else { return false }
                return !r.passed
            }
        }.map(\.id))
    }

    func openStudio(_ tab: StudioTab = .look) {
        studioTab = tab
        studioOpen = true
    }

    /// The folders the Studio's Folders tab lists: no work folder in demo mode.
    func studioRows(demo: Bool) -> [OwnModRow] { own.filter { !(demo && isWork($0.folder)) } }

    /// The Studio's header and the page's line: on in how many of the folders found, nil before
    /// they are read.
    func studioOnLine(demo: Bool) -> String? {
        guard ownLoaded else { return nil }
        return OwnModRow.onLine(studioRows(demo: demo))
    }

    // MARK: More: mods and config (2026-10-17)

    /// The More disclosure's remembered choice: true or false once the user opened or closed it.
    static let moreExpandedKey = "modsMoreExpanded"
    private let moreStore: DefaultsStore
    /// The choice as kept, nil until the user makes one.
    private var moreChoice: Bool?
    /// A link or search into More (the Mods and plugins anchor) opens it for this window only.
    var moreOpenedByLink = false

    /// More starts open when there is something in it for this user: a kept mod, a proposal or
    /// install request waiting, or more than one folder. One folder and nothing waiting is the
    /// first-timer's case, and the header stands alone.
    nonisolated static func moreStartsOpen(kept: Int, waiting: Int, folders: Int) -> Bool {
        kept > 0 || waiting > 0 || folders > 1
    }

    /// Whether More is open: a link's opening, else the kept choice, else the default from what
    /// the page has read. `waiting` is ModProposalStore's pending proposals and install requests.
    func moreExpanded(demo: Bool, waiting: Int) -> Bool {
        if moreOpenedByLink { return true }
        if let moreChoice { return moreChoice }
        return Self.moreStartsOpen(kept: kept.count, waiting: waiting, folders: studioRows(demo: demo).count)
    }

    /// The user opened or closed More: kept for next time.
    func setMoreExpanded(_ open: Bool) {
        moreOpenedByLink = false
        moreChoice = open
        moreStore.set(open, forKey: Self.moreExpandedKey)
    }

    /// The folder of the mod this copy of Sanduhr carries, nil in a build without it.
    var ownModDir: String? {
        guard installer.scripts.hasMod else { return nil }
        return installer.scripts.source?.appendingPathComponent(IntegrationScripts.modPath).path
    }

    /// Check for the About tab: `claude plugin validate --json` on the mod this Sanduhr carries.
    func checkOwnMod() async {
        guard ownLoaded, let dir = ownModDir, !checkingOwn else { return }
        guard let claude else {
            ownCheck = .unavailable
            return
        }
        checkingOwn = true
        defer { checkingOwn = false }
        ownCheck = await Task.detached(priority: .userInitiated) { ModCheck.run(claude: claude, dir: dir) }.value
    }

    /// A card shows in full when it was opened, or when the search found it only by what it brings.
    func isOpen(_ card: CatalogCard) -> Bool {
        openCards.contains(card.id) || ConfigCatalog.matchesInsideOnly(card, search: filter.search)
    }

    /// Whether the search's match is inside this card's contents, so Show opens with it.
    func showsContents(_ card: CatalogCard) -> Bool {
        openContents.contains(card.id) || ConfigCatalog.matchesInsideOnly(card, search: filter.search)
    }

    /// Right and Left on a Catalog line (slice A): open and fold, as a list does.
    func openCard(_ id: String) { openCards.insert(id) }
    func foldCard(_ id: String) { openCards.remove(id) }

    func toggleCard(_ id: String) {
        if openCards.contains(id) { openCards.remove(id) } else { openCards.insert(id) }
    }

    /// Expand All opens every card shown (the Available group's lines stay one line each).
    func expandAll(demo: Bool) {
        openCards.formUnion(shownCards(demo: demo).map(\.id))
    }

    func collapseAll() { openCards.removeAll() }

    /// The cards to show: filters, search and (demo mode) no work-only card.
    func shownCards(demo: Bool) -> [CatalogCard] {
        ConfigCatalog.filter(catalog, filter, demo: demo, failedChecks: failedChecks)
    }

    /// The shown cards, grouped (slice 1c).
    func sections(demo: Bool) -> [CatalogSection] {
        ConfigCatalog.sections(shownCards(demo: demo), failedChecks: failedChecks, byLoad: sortByLoad)
    }

    /// Which controls apply (polish 2026-10-17): the whole catalog's count and the folders demo
    /// mode shows, and whether a filter is set.
    func controls(demo: Bool) -> ModsControls {
        ModsControls(cards: ConfigCatalog.filter(catalog, CatalogFilter(), demo: demo).count,
                     folders: filterSeats(demo: demo).count, narrowed: filter.narrows)
    }

    /// The tab bar's segments: Compare with two or more folders. `tab` may still be `.compare`
    /// (the smoke hook); the view then says what it needs.
    func tabs(demo: Bool) -> [ModsTab] {
        let controls = controls(demo: demo)
        return ModsTab.allCases.filter { $0 != .compare || controls.compare }
    }

    /// The seats' session loads the Catalog shows: no work seat in demo mode.
    func shownLoads(demo: Bool) -> [(seat: Seat, load: SessionLoad)] {
        zip(seats, loads).filter { !(demo && $0.0.isWork) }.map { (seat: $0.0, load: $0.1) }
    }

    // MARK: Compare (slice 1d)

    /// The two seats Compare shows, as picked; nil picks the first shown seats.
    var compareA: String?
    var compareB: String?
    var openBuckets: Set<SeatComparison.Bucket> = Set(SeatComparison.Bucket.allCases.filter(\.startsOpen))

    /// The two seats to compare: the picks when both are shown and differ, else the first two
    /// shown seats (no work seat in demo mode). Nil with fewer than two.
    func comparePair(demo: Bool) -> (a: Seat, b: Seat)? {
        Self.pair(filterSeats(demo: demo), a: compareA, b: compareB)
    }

    /// The pair from `shown` for picks `a` and `b`: a pick that isn't shown (gone, or a work
    /// seat in demo mode) falls back to the first shown seat that isn't the other one.
    nonisolated static func pair(_ shown: [Seat], a pickA: String?, b pickB: String?) -> (a: Seat, b: Seat)? {
        guard let a = shown.first(where: { $0.path == pickA }) ?? shown.first,
              let b = shown.first(where: { $0.path == pickB && $0.path != a.path }) ?? shown.first(where: { $0.path != a.path })
        else { return nil }
        return (a, b)
    }

    /// A seat picked on one side: picking the seat the other side shows swaps the two.
    func pickCompare(_ path: String, first: Bool, current: (a: Seat, b: Seat)) {
        (compareA, compareB) = Self.picked(path, first: first, a: current.a.path, b: current.b.path)
    }

    nonisolated static func picked(_ path: String, first: Bool, a: String, b: String) -> (String, String) {
        if first { return path == b ? (b, a) : (path, b) }
        return path == a ? (b, a) : (a, path)
    }

    func comparison(demo: Bool) -> SeatComparison? {
        guard let pair = comparePair(demo: demo) else { return nil }
        let cards = demo ? catalog.compactMap { $0.demoView() } : catalog
        return ConfigCatalog.compare(cards, a: pair.a.path, b: pair.b.path)
    }

    func load(_ path: String) -> SessionLoad? { loads.first { $0.seat == path } }

    /// A chip's click: By Seat at that seat and that extension.
    func open(_ chip: CatalogChip, card: CatalogCard) {
        focus = ModsFocus(seat: chip.seat, card: card.id, name: card.name)
        selectedSeat = chip.seat
        openSections.insert(card.kind == .mcpServer ? SeatSectionID.mcp.rawValue : SeatSectionID.extensions.rawValue)
        tab = .bySeat
    }

    /// Each seat's eight sections, in seat order.
    private(set) var reports: [SeatReport] = []
    /// Seats a running `claude` of this user uses, and processes that couldn't be read.
    private(set) var running: Set<String> = []
    private(set) var unreadableProcesses = 0
    /// The Claude Code version on PATH, from the native installer's versioned folder; nil when
    /// that can't say (the CLI is never run to ask).
    private(set) var claudeVersion: String?
    /// The seat Sanduhr's own `CLAUDE_CONFIG_DIR` names (Sanduhr runs from the Dock, so usually none).
    private(set) var environmentSeat: String?
    /// The seat By Seat shows. Moving to another folder clears the notes: they were the last one's.
    var selectedSeat: String? {
        didSet { if oldValue != selectedSeat { clearConfigNotes() } }
    }
    /// By Seat's sections that are open (SeatSectionID raw values), kept while the window lives.
    var openSections: Set<String> = []

    /// A seat asked for by number (the smoke hook) before the seats were read.
    var pendingSeat: Int?
}
