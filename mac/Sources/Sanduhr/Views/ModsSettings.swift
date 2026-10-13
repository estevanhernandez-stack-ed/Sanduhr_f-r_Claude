import AppKit
import SwiftUI

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
    private(set) var checks: [String: ModCheckOutcome] = [:]
    private(set) var checking: Set<String> = []

    /// Sanduhr's own mod in each folder, the app's version of it, and what the last action said.
    private(set) var own: [OwnModRow] = []
    private(set) var appMod: AppModVersion?
    private(set) var ownNotes: [String: OwnModNote] = [:]
    private(set) var ownBusy: Set<String> = []
    /// A folder whose list has no entry, waiting for On's confirmation.
    var confirmOn: String?
    /// An update that can do more than the version in use, waiting for an answer.
    var updateQuestion: OwnModUpdateQuestion?

    let home = NSHomeDirectory()
    var installer = IntegrationInstaller.standard

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
    func load(linked: [String]) async {
        guard !loading else { return }
        loading = true
        defer { loading = false }
        let home = self.home
        let installer = self.installer
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
            return read
        }.value
        claude = first.claude
        own = first.own
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

    /// The Settings window closed: Mods & Config reopens on its Catalog, nothing focused.
    func windowClosed() {
        tab = .catalog
        focus = nil
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
    /// The cards opened from their compact line (item 75, slice A), kept while the window lives.
    var openCards: Set<String> = []
    var sortByLoad = false
    /// The Mod Studio sheet is up (item 75, slice B).
    var studioOpen = false
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

    /// The Studio's header: on in how many of the folders found, nil before they are read.
    var studioOnLine: String? {
        guard ownLoaded, !own.isEmpty else { return nil }
        return "On in \(own.filter(\.state.isOn).count) of \(own.count) folder\(own.count == 1 ? "" : "s")"
    }

    /// A card shows in full when it was opened, or when the search found it only by what it brings.
    func isOpen(_ card: CatalogCard) -> Bool {
        openCards.contains(card.id) || ConfigCatalog.matchesInsideOnly(card, search: filter.search)
    }

    /// Whether the search's match is inside this card's contents, so Show opens with it.
    func showsContents(_ card: CatalogCard) -> Bool {
        openContents.contains(card.id) || ConfigCatalog.matchesInsideOnly(card, search: filter.search)
    }

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

    // MARK: By Seat (item 71, slice 1b)

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
    /// The seat By Seat shows.
    var selectedSeat: String?
    /// By Seat's sections that are open (SeatSectionID raw values), kept while the window lives.
    var openSections: Set<String> = []

    /// The seats By Seat offers: in demo mode, no work seat.
    func pickerSeats(demo: Bool) -> [Seat] { seats.filter { !(demo && $0.isWork) } }

    /// The selected seat, else the first one offered.
    func shownSeat(demo: Bool) -> Seat? {
        let offered = pickerSeats(demo: demo)
        return offered.first { $0.path == selectedSeat } ?? offered.first
    }

    func report(for seat: String) -> SeatReport? { reports.first { $0.seat == seat } }

    /// Drift against the seats demo mode still shows (no work seat in demo mode).
    func shownDrift(_ report: SeatReport, demo: Bool) -> [SeatReport.Drift] {
        report.drift.filter { !(demo && isWork($0.other)) }
    }

    /// The report as By Seat summarizes it in demo mode: drift against shown seats only.
    func shownReport(_ report: SeatReport, demo: Bool) -> SeatReport {
        var r = report
        r.drift = shownDrift(report, demo: demo)
        return r
    }

    func label(of path: String) -> String { seats.first { $0.path == path }?.label ?? (path as NSString).lastPathComponent }

    func mark(of path: String) -> SeatMark { seats.first { $0.path == path }?.mark ?? .unmarked }

    /// The account linked to a seat, by its label (Sanduhr's own name for it).
    func linkedAccount(_ path: String) -> String? { AccountData.account(linkedTo: path, in: store) }

    /// A seat's counts with its running dot, for state.yaml.
    func counts(_ seat: String) -> SeatCounts {
        var c = report(for: seat)?.counts ?? SeatCounts()
        c.running = running.contains(seat)
        return c
    }

    /// The version in a native install's path (`…/versions/2.1.296`), nil otherwise.
    nonisolated static func claudeVersion(_ path: String?) -> String? {
        guard let path else { return nil }
        let last = URL(fileURLWithPath: path).resolvingSymlinksInPath().lastPathComponent
        return last.range(of: #"^\d+\.\d+\.\d+"#, options: .regularExpression) != nil ? last : nil
    }

    /// A seat asked for by number (the smoke hook) before the seats were read.
    private var pendingSeat: Int?

    /// A tab, and on By Seat the nth seat (from 1), as the smoke hook asks for it.
    func showTab(_ tab: ModsTab, seat: Int?) {
        self.tab = tab
        pendingSeat = seat
        applyPendingSeat()
    }

    private func applyPendingSeat() {
        guard let n = pendingSeat, !seats.isEmpty else { return }
        pendingSeat = nil
        let seat = seats[min(n, seats.count) - 1]
        selectedSeat = seat.path
        focus = ModsFocus(seat: seat.path, card: "", name: "")
    }

    /// The scroll id of a seat's place on By Seat.
    nonisolated static func seatAnchor(_ path: String) -> String { "seat:" + path }

    func isWork(_ path: String) -> Bool { seats.first { $0.path == path }?.isWork ?? false }

    /// The label and mark a person gives a seat (Sanduhr preferences, never written into it).
    func setLabel(_ label: String, for seat: String) {
        SeatPreferences.setLabel(label, for: seat, in: store)
        refreshSeats()
    }

    func setMark(_ mark: SeatMark, for seat: String) {
        SeatPreferences.setMark(mark, for: seat, in: store)
        refreshSeats()
    }

    /// Add Folder… (item 71, slice 0): a Claude Code folder discovery doesn't find (one outside
    /// the home folder, or named otherwise), remembered as a Sanduhr preference. Nothing is
    /// written into the folder.
    func addFolder(linked: [String]) async {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.directoryURL = URL(fileURLWithPath: home)
        panel.prompt = "Add"
        panel.message = "Choose a Claude Code folder (one with a projects folder or a .claude.json)."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard ClaudeCodeFolders.looksLikeHome(url.path, home: home) else {
            addNote = "That folder doesn't look like a Claude Code folder: it has no projects folder and no .claude.json."
            return
        }
        addNote = nil
        SeatPreferences.add(url.path, in: store)
        await load(linked: linked)
    }

    /// Runs `claude plugin validate --json` on the item's folder (never `plugin test`).
    func check(_ item: ModItem) async {
        guard let path = item.path, !checking.contains(item.id) else { return }
        checking.insert(item.id)
        defer { checking.remove(item.id) }
        let claude = self.claude
        let outcome = await Task.detached(priority: .userInitiated) { ModCheck.run(claude: claude, dir: path) }.value
        checks[item.id] = outcome
    }

    func display(_ path: String) -> String { ClaudeCodeFolders.Folder(path: path).display(home: home) }

    // MARK: Sanduhr's own mod (slice 2)

    /// The switch: On in a folder whose list has no entry asks first (it adds one).
    func requestSwitch(_ row: OwnModRow, on: Bool, linked: [String]) {
        if on && !row.state.listed {
            confirmOn = row.folder
            return
        }
        Task { await setOwn(on: on, folder: row.folder, linked: linked) }
    }

    func setOwn(on: Bool, folder: String, linked: [String]) async {
        await act(folder, linked: linked) { installer, home in
            try installer.switchOwnMod(on: on, folder: folder)
            let now = installer.ownModState(folder: folder)
            let found = ModOverrides.find(key: IntegrationInstaller.ownModKey, folder: folder, home: home)
            if let text = ModOverrides.message(found, on: now.isOn, home: home) { return OwnModNote(text: text, kind: .warning) }
            return OwnModNote(text: ModsPageModel.takesEffect, kind: .info)
        }
    }

    func update(folder: String, confirmed: Bool, linked: [String]) async {
        let claude = self.claude
        let note = await act(folder, linked: linked) { installer, _ in
            let outcome = try installer.updateOwnMod(folder: folder, confirmed: confirmed) { old, new in
                ModCapabilities.added(old: old, new: new) { dir in
                    if case .report(let r) = ModCheck.run(claude: claude, dir: dir), r.passed { return r }
                    return nil
                }
            }
            switch outcome {
            case .upToDate: return OwnModNote(text: "Already the version this copy of Sanduhr carries.", kind: .info)
            case .updated: return OwnModNote(text: "Updated. " + ModsPageModel.takesEffect, kind: .info)
            case .needsConsent(let added): return OwnModNote(text: "", kind: .info, added: added)
            }
        }
        if let added = note?.added { updateQuestion = OwnModUpdateQuestion(folder: folder, added: added) }
    }

    func remove(folder: String, linked: [String]) async {
        await act(folder, linked: linked) { installer, _ in
            try installer.remove(.meters, folder: folder)
            return OwnModNote(text: "Removed: this folder's settings.json is back to what it was before Sanduhr's mod. " + ModsPageModel.takesEffect, kind: .info)
        }
    }

    /// Installs the mod's entry after the consent sheet, or (`updating`) moves an older entry to
    /// this Sanduhr's scripts. The mod runs inside Claude Code, so no python3 is needed.
    func install(folder: String, updating: Bool, linked: [String]) async {
        await act(folder, linked: linked) { installer, _ in
            try installer.install(.meters, folder: folder, python: "")
            return OwnModNote(text: (updating ? "Updated. " : "Installed. ") + ModsPageModel.takesEffect, kind: .info)
        }
    }

    /// Runs one write off the main thread, then reads the page again. A note carrying `added`
    /// is a question, not a note.
    @discardableResult
    private func act(_ folder: String, linked: [String],
                     _ body: @escaping @Sendable (IntegrationInstaller, String) throws -> OwnModNote?) async -> OwnModNote? {
        guard !ownBusy.contains(folder) else { return nil }
        ownBusy.insert(folder)
        let installer = self.installer, home = self.home
        let note = await Task.detached(priority: .userInitiated) { () -> OwnModNote? in
            do { return try body(installer, home) } catch { return OwnModNote(failure: error) }
        }.value
        ownNotes[folder] = note?.added == nil ? note : nil
        ownBusy.remove(folder)
        await load(linked: linked)
        return note
    }

    /// Forget Folder: Sanduhr stops listing a folder it was given with Add Folder…. The folder
    /// itself is untouched; one discovery finds stays listed.
    func forgetFolder(_ path: String, linked: [String]) async {
        SeatPreferences.forget(path, in: store)
        await load(linked: linked)
    }

    nonisolated static func receiptsSkippedText(_ n: Int) -> String {
        let what = n == 1 ? "One of Sanduhr's install records" : "\(n) of Sanduhr's install records"
        return "\(what) couldn't be read and \(n == 1 ? "was" : "were") set aside, kept as written. What \(n == 1 ? "it" : "they") recorded can't be undone from here; every other record still can."
    }

    /// A seat's settings.json that doesn't parse: unreadable, never empty.
    nonisolated static func unreadableText(_ why: String) -> String {
        "settings.json is unreadable: it \(why). Claude Code can't read it either (comments and trailing commas aren't allowed), so what it holds isn't listed here."
    }

    nonisolated static let takesEffect = "Takes effect in new Claude Code sessions (or after /reload-plugins)."

    static let cliMissing = "Check needs Claude Code's command line (claude), and it wasn't found on your PATH or in the usual install folders (~/.local/bin, /opt/homebrew/bin, /usr/local/bin)."
}

/// What one read of every seat found, handed from the background to the page.
struct ModsPageRead: Sendable {
    var paths: [String]
    var scans: [SeatScan]
    var inventory: [ModFolderInventory]
    var claude: String?
    var own: [OwnModRow]
    var app: AppModVersion?
    var notes: [String: String]
    var configs: [String: [ClaudeCodeFolders.ConfigCandidate]]
    var skipped: Int
    var origins: [String: Seat.Origin] = [:]
    var reports: [SeatReport] = []
    var running: Set<String> = []
    var unreadableProcesses = 0
    var version: String?
    var environmentSeat: String?
}

/// Mods & Config's tabs (item 71, slice 1; Compare in slice 1d).
enum ModsTab: String, CaseIterable, Sendable {
    case catalog, bySeat, compare

    var title: String {
        switch self {
        case .catalog: "Catalog"
        case .bySeat: "By Seat"
        case .compare: "Compare"
        }
    }

    /// state.yaml's word.
    var debugName: String {
        switch self {
        case .catalog: "catalog"
        case .bySeat: "seat"
        case .compare: "compare"
        }
    }
}

/// Where a chip's click lands on By Seat.
struct ModsFocus: Equatable, Sendable {
    let seat: String
    let card: String
    let name: String
}

/// A terminal-styled sketch of where a mod draws in Claude Code (item 64): blocks stand in for
/// its content, so nothing here is live text, and nothing of the mod runs.
enum ModSketch {
    static let dim = "\u{1B}[2m", reset = "\u{1B}[0m"
    static let cyan = "\u{1B}[36m", yellow = "\u{1B}[33m", magenta = "\u{1B}[35m", green = "\u{1B}[32m"

    /// The sketch for `touches`, top to bottom as Claude Code lays it out; nil when the mod
    /// draws nothing.
    static func text(name: String, touches: ModTouches) -> String? {
        let s = Set(touches.surfaces)
        guard !s.isEmpty else { return nil }
        var lines = [dim + "⏺ ░░░░░░░░░░░░░░░░░░░░ ░░░░░░░░░" + reset]
        if s.contains(.pane) {
            lines.append(magenta + "┌ \(name) ─────────────────────" + reset)
            lines.append(magenta + "│ " + reset + dim + "░░░░░░░░░░ ░░░░░░░░" + reset)
            lines.append(magenta + "└──────────────────────────────" + reset)
        }
        if s.contains(.band) { lines.append(cyan + "▌ \(name) " + reset + cyan + "██████████░░░░░░  ░░░░" + reset) }
        lines.append("> " + dim + "░░░░░░░" + reset)
        if s.contains(.status) { lines.append(yellow + "⚠ \(name): " + reset + dim + "░░░░░░░░" + reset) }
        if s.contains(.toast) { lines.append(green + "  ◆ \(name) " + reset + dim + "░░░░░░░░░░░░" + reset) }
        if s.contains(.commands) {
            let names = touches.commands.isEmpty ? ["░░░░"] : touches.commands.prefix(4).map { $0 }
            lines.append(dim + names.map { "/" + $0 }.joined(separator: "  ") + reset)
        }
        return lines.joined(separator: "\n")
    }

    static func lineCount(_ touches: ModTouches) -> Int {
        text(name: "", touches: touches)?.split(separator: "\n", omittingEmptySubsequences: false).count ?? 0
    }
}

/// Settings, Mods & Config (item 64; raw value `mods`): Sanduhr's own mod first, Meters above the
/// prompt, with its switch, Update and Remove per folder (its one home since 2026-10-08; slice 2
/// had it on Claude Code), then every mod and plugin each Claude Code folder loads, what each
/// draws and can reach, and Check (Claude Code's own validator) for a risk card. Other mods are
/// read-only.
struct ModsSettings: View {
    var vm: UsageViewModel
    var model: ModsPageModel
    @State private var consent: MetersConsent?

    private var linked: [String] { vm.realAccountLabels.compactMap { vm.dataChoices(for: $0).folder } }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    SanduhrModSection(model: model, linked: linked, install: { consent = MetersConsent(folder: $0) })
                        .settingsAnchor(SettingsAnchor.metersMod)
                    if model.loaded && model.claude == nil {
                        Text(ModsPageModel.cliMissing)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    // Catalog | By Seat (item 71, slice 1), right above what each shows.
                    ModsTabBar(model: model)
                        .settingsAnchor(SettingsAnchor.inventory)
                    tabContent
                    ModsFolderActions(model: model, linked: linked)
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)
                .padding(20)
            }
            .onChange(of: model.focus) {
                guard let f = model.focus else { return }
                DispatchQueue.main.async { withAnimation { proxy.scrollTo(ModsPageModel.seatAnchor(f.seat), anchor: .top) } }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task { await model.load(linked: linked) }
        .ownModQuestions(model, linked: linked)
        .sheet(isPresented: Binding(get: { model.studioOpen }, set: { model.studioOpen = $0 })) {
            ModStudioSheet(model: .live(), onLine: model.studioOnLine, close: { model.studioOpen = false })
        }
        .sheet(item: $consent) { c in
            MetersConsentSheet(folder: model.display(c.folder),
                               file: model.display(model.installer.configFile(.meters, folder: c.folder)),
                               install: {
                                   consent = nil
                                   Task { await model.install(folder: c.folder, updating: false, linked: linked) }
                               },
                               cancel: { consent = nil })
        }
    }

    @ViewBuilder
    private var tabContent: some View {
        switch model.tab {
        case .catalog:
            ModsCatalogView(model: model, demo: vm.demo)
        case .bySeat:
            ModsBySeatView(model: model, demo: vm.demo, linked: linked)
        case .compare:
            ModsCompareView(model: model, demo: vm.demo)
        }
    }
}

/// Read Again, Add Folder… and what the last read or add said, under either tab.
private struct ModsFolderActions: View {
    var model: ModsPageModel
    let linked: [String]

    var body: some View {
        if model.receiptsSkipped > 0 {
            Text(ModsPageModel.receiptsSkippedText(model.receiptsSkipped))
                .font(.caption)
                .foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        }
        HStack(spacing: 8) {
            Button(model.loading ? "Reading…" : "Read Again") { Task { await model.load(linked: linked) } }
                .disabled(model.loading)
            Button("Add Folder…") { Task { await model.addFolder(linked: linked) } }
                .disabled(model.loading)
                .help("Add a Claude Code folder Sanduhr didn't find. Sanduhr remembers it; nothing is written into the folder.")
        }
        if let note = model.addNote {
            Text(note).font(.caption).foregroundStyle(Color.hex("f87171"))
        }
    }
}

struct ModsIntro: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Mods and plugins").font(.subheadline.weight(.semibold))
            Text("Everything each Claude Code folder loads: mods (plugins with a hooks module that draw in Claude Code) and plain plugins, from the plugin folders its settings list, its installed plugins, its skills folder and the mods a session made. Sanduhr finds them by reading files: no mod runs. Other mods are read-only here: Sanduhr's own switches per folder above, and switching other mods comes later. Check asks Claude Code's own validator, which reads a mod without running it, what the mod hooks and calls.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// A seat's mods and plugins (item 64's list), now Extensions' Mods subsection on By Seat
/// (item 71, slice 1b): each with where it loads from, what it touches, its sketch and Check.
struct ModsSeatMods: View {
    let seat: String
    var model: ModsPageModel

    var body: some View {
        let items = model.inventory.first { $0.folder == seat }?.items ?? []
        VStack(alignment: .leading, spacing: 10) {
            ModsIntro()
            if items.isEmpty {
                Text("Loads no mods or plugins.").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(items) { item in
                ModRow(item: item, model: model)
                    .background(RoundedRectangle(cornerRadius: 6).fill(Color.accentColor.opacity(lit(item) ? 0.18 : 0)).padding(-4))
                if item.id != items.last?.id { Divider() }
            }
        }
    }

    /// The item a Catalog chip opened this seat for.
    private func lit(_ item: ModItem) -> Bool {
        guard let f = model.focus, f.seat == seat else { return false }
        return item.name == f.name
    }
}

/// One mod or plugin: what it is, where it loads from, what it touches, its sketch and Check.
private struct ModRow: View {
    let item: ModItem
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ModRowHeader(item: item)
            if !item.description.isEmpty {
                Text(item.description)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            ModRowWhere(item: item, model: model)
            ModRowOwnership(item: item)
            ModRowTouches(touches: item.touches)
            if item.isMod, let sketch = ModSketch.text(name: item.name, touches: item.touches) {
                ModSketchView(name: item.name, sketch: sketch, lines: ModSketch.lineCount(item.touches))
            }
            ModCheckRow(item: item, model: model)
        }
    }
}

private struct ModRowHeader: View {
    let item: ModItem

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(item.name).font(.body.weight(.semibold))
            if let v = item.version, !v.isEmpty {
                Text(v).font(.caption.monospaced()).foregroundStyle(.secondary)
            }
            Text(item.isMod ? "Mod" : "Plugin")
                .font(.caption2.weight(.semibold))
                .padding(.horizontal, 6)
                .padding(.vertical, 1)
                .background(Capsule().fill(Color.secondary.opacity(0.18)))
            Spacer(minLength: 8)
            Text(item.state.title)
                .font(.caption)
                .foregroundStyle(stateColor)
        }
    }

    private var stateColor: Color {
        switch item.state {
        case .on: Color.hex("4ade80")
        case .off: .secondary
        case .session: .orange
        case .missing: Color.hex("f87171")
        }
    }
}

private struct ModRowWhere: View {
    let item: ModItem
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(item.originTitle).font(.caption)
            if let path = item.path {
                Text(model.display(path) + (item.state == .missing ? " (not found)" : ""))
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
                    .lineLimit(2)
                    .truncationMode(.middle)
            } else {
                Text("Turned \(item.state == .on ? "on" : "off") here, but its files aren't in this folder's installed plugins.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// Whose mod it is. Sanduhr's own switches at the top of this page; the rest are read-only for now.
private struct ModRowOwnership: View {
    let item: ModItem

    var body: some View {
        switch item.ownership {
        case .sanduhrs:
            Text("Sanduhr's own mod. It switches per folder at the top of this page, \(SettingsNames.metersAbovePrompt).")
                .font(.caption)
                .foregroundStyle(Color.hex("a78bfa"))
                .fixedSize(horizontal: false, vertical: true)
        case .copyOfSanduhrs:
            Text("A copy of Sanduhr's mod outside Sanduhr's folder: read-only here.")
                .font(.caption)
                .foregroundStyle(.secondary)
        case .others:
            EmptyView()
        }
    }
}

private struct ModRowTouches: View {
    let touches: ModTouches

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            if !touches.surfaces.isEmpty {
                Text("Draws: " + touches.surfaces.map(title).joined(separator: ", ")).font(.caption)
            }
            if !touches.capabilities.isEmpty {
                Text("Its code " + touches.capabilities.map(\.title).joined(separator: ", ") + ".")
                    .font(.caption)
                    .foregroundStyle(.orange)
            }
        }
    }

    private func title(_ s: ModSurface) -> String {
        guard s == .commands, !touches.commands.isEmpty else { return s.title }
        return s.title + " (" + touches.commands.map { "/" + $0 }.joined(separator: ", ") + ")"
    }
}

/// The surfaces as a terminal sketch, labeled as one.
private struct ModSketchView: View {
    let name: String
    let sketch: String
    let lines: Int

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            TerminalPreviewFrame(sketch, title: "Claude Code", maxLines: max(lines, 1))
                .frame(maxWidth: 520, alignment: .leading)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Sketch of where \(name) draws in Claude Code")
            Text("Sketch: drawn by the mod in Claude Code")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
    }
}

/// Check and its answer.
private struct ModCheckRow: View {
    let item: ModItem
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Button(running ? "Checking…" : "Check") { Task { await model.check(item) } }
                    .disabled(!available || running)
                    .help(help)
                if !available {
                    Text(reason).font(.caption).foregroundStyle(.secondary)
                }
            }
            if let outcome = model.checks[item.id] { ModRiskCard(outcome: outcome) }
        }
    }

    private var running: Bool { model.checking.contains(item.id) }
    private var available: Bool { model.claude != nil && item.path != nil && item.state != .missing }

    private var reason: String {
        if item.path == nil || item.state == .missing { return "Nothing to check: its folder isn't here." }
        return model.loaded ? "Unavailable: Claude Code's command line wasn't found." : ""
    }

    private var help: String {
        if model.claude == nil { return ModsPageModel.cliMissing }
        return "Runs claude plugin validate --json on this folder. It reads the files and runs none of the mod's code."
    }
}

/// The validator's answer as a risk card.
private struct ModRiskCard: View {
    let outcome: ModCheckOutcome

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            switch outcome {
            case .report(let r): ModReportView(report: r)
            case .unavailable: Text(ModsPageModel.cliMissing).font(.caption)
            case .timedOut:
                Text("claude plugin validate didn't answer within 10 seconds, so Sanduhr stopped it.").font(.caption)
            case .failed(let why): Text("Check couldn't read a report: \(why)").font(.caption)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 6).fill(tint.opacity(0.12)))
    }

    private var tint: Color {
        guard case .report(let r) = outcome else { return .secondary }
        if !r.passed { return Color.hex("f87171") }
        switch r.risk {
        case .low: return Color.hex("4ade80")
        case .medium: return .orange
        case .high: return Color.hex("f87171")
        }
    }
}

private struct ModReportView: View {
    let report: ModCheckReport

    var body: some View {
        Text(headline).font(.caption.weight(.semibold))
        ForEach(report.reasons, id: \.self) { Text("• " + $0).font(.caption) }
        ForEach(report.errors, id: \.self) { Text("Error: " + $0).font(.caption).foregroundStyle(Color.hex("f87171")) }
        ForEach(report.warnings, id: \.self) { Text("Warning: " + $0).font(.caption).foregroundStyle(.orange) }
        if !report.hooks.isEmpty { detail("Hooks", report.hooks) }
        if !report.calls.isEmpty { detail("Calls", report.calls) }
        ForEach(report.notes, id: \.self) { Text($0).font(.caption).foregroundStyle(.secondary) }
        Text("From claude plugin validate, which read the files without running them.")
            .font(.caption2)
            .foregroundStyle(.secondary)
    }

    private var headline: String {
        report.passed ? report.risk.title + " · Claude Code would load it"
            : report.risk.title + " · Claude Code would refuse it (\(report.errors.count) error\(report.errors.count == 1 ? "" : "s"))"
    }

    private func detail(_ title: String, _ values: [String]) -> some View {
        Text("\(title): " + values.joined(separator: ", "))
            .font(.caption.monospaced())
            .foregroundStyle(.secondary)
    }
}

/// The page's summary card (item 68's pattern): the counts across folders, on the preview
/// wallpaper.
struct ModsSummaryCard: View {
    var model: ModsPageModel

    var body: some View {
        SettingsPreviewCard(kind: .mods, label: label) {
            VStack(spacing: 10) {
                if model.loaded {
                    HStack(spacing: 14) {
                        let tiles = self.tiles
                        ForEach(tiles.indices, id: \.self) { i in ModsTile(number: tiles[i].0, title: tiles[i].1) }
                    }
                } else {
                    Text("Reading your Claude Code folders…").foregroundStyle(.white.opacity(0.8))
                }
                Text("Sanduhr's own mod switches here, per folder; other mods are read-only.")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(12)
        }
    }

    private var tiles: [(number: Int, title: String)] { model.counts.tiles }

    private var label: String {
        guard model.loaded else { return "Reading your Claude Code folders." }
        return model.counts.summary
    }
}

private struct ModsTile: View {
    let number: Int
    let title: String

    var body: some View {
        VStack(spacing: 2) {
            Text("\(number)").font(.system(size: 30, weight: .semibold, design: .rounded)).foregroundStyle(.white)
            Text(title).font(.caption).foregroundStyle(.white.opacity(0.75))
        }
        .frame(minWidth: 70)
        .padding(.vertical, 8)
        .padding(.horizontal, 10)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.white.opacity(0.08)))
    }
}
