import Foundation
import Observation

/// What Settings ▸ Widgets shows and does (spec 3): the catalog, the installed packs, install,
/// update and uninstall, and the companion's two choices. Nothing here runs on its own: the
/// catalog is fetched when the page appears or on "Check the catalog again", and a pack is
/// downloaded only on Install or Update.
@MainActor
@Observable
final class WidgetsModel {
    /// What the catalog row offers for one pack.
    enum Offer: Equatable {
        case install
        case update
        case installed
        /// The id is installed from another source, so the catalog's pack can't replace it.
        case madeHere
    }

    /// One installed pack, as the page lists it.
    struct InstalledRow: Identifiable, Equatable {
        let id: String
        let name: String
        let kind: String
        let version: String
        let madeHere: Bool

        /// "from the catalog" or "made here".
        var sourceText: String { madeHere ? "made here" : "from the catalog" }
    }

    static let effectsNote = "Effects packs don't draw yet."

    /// The verified catalog's packs, newest refresh. Kept when a later refresh fails.
    private(set) var catalog: [WidgetCatalogEntry] = []
    /// One quiet line when the last refresh failed, nil otherwise.
    private(set) var catalogNote: String?
    private(set) var checking = false
    /// The ids being installed, updated or removed right now.
    private(set) var busy: Set<String> = []
    /// The last install or uninstall that did not go through, in a sentence.
    private(set) var notice: String?
    private(set) var installed: [InstalledRow] = []
    private(set) var companion: CompanionSettings
    /// The last "Use my own art..." outcome in a sentence (a check that failed, or the success line), nil before one.
    private(set) var ownArtStatus: String?

    let store: WidgetStore
    private let defaults: UserDefaults
    private let makeClient: (WidgetStore) -> WidgetCatalogClient
    private let changed: () -> Void
    private var client: WidgetCatalogClient?
    @ObservationIgnored private nonisolated(unsafe) var packsObserver: NSObjectProtocol?

    /// - Parameters:
    ///   - store: The installed packs.
    ///   - defaults: Where the companion's choices live (the Desk's suite).
    ///   - makeClient: The catalog client over the network; tests pass one over a dictionary.
    ///   - changed: Runs after the installed packs changed, so theme and palette lists re-read.
    init(store: WidgetStore = .shared, defaults: UserDefaults = .desk,
         makeClient: @escaping (WidgetStore) -> WidgetCatalogClient = WidgetsModel.networkClient,
         changed: @escaping () -> Void = WidgetsModel.refreshPackLists) {
        self.store = store
        self.defaults = defaults
        self.makeClient = makeClient
        self.changed = changed
        self.companion = CompanionSettings.load(defaults)
        reloadInstalled()
        // A pack installed somewhere else (a Claude-made widget applied from the review banner) shows at once: the lists
        // are read again from the widgets folder. The catalog is not fetched. Delivered on the posting thread; the
        // app posts from the main thread, and anything else hops there.
        packsObserver = NotificationCenter.default.addObserver(forName: .widgetPacksDidChange, object: nil, queue: nil) { [weak self] note in
            let from = note.object as AnyObject?
            if Thread.isMainThread {
                MainActor.assumeIsolated { self?.packsChangedElsewhere(from) }
            } else {
                Task { @MainActor in self?.packsChangedElsewhere(from) }
            }
        }
    }

    deinit {
        if let packsObserver { NotificationCenter.default.removeObserver(packsObserver) }
    }

    /// `.widgetPacksDidChange` arrived: re-read the installed packs and the companion's pick (Make it the companion
    /// saves one). Nothing is posted back, and `changed` is the poster's to run.
    private func packsChangedElsewhere(_ from: AnyObject?) {
        guard from !== self else { return }
        reloadInstalled()
    }

    static func networkClient(_ store: WidgetStore) -> WidgetCatalogClient {
        WidgetCatalogClient(fetch: WidgetDownloader().fetch, marker: store)
    }

    /// The default `changed`: the Theme menu and the gallery re-read the registry (the view
    /// model's tick, bumped by the caller that owns it, redraws them), and the message palettes.
    static func refreshPackLists() {
        UserThemes.reload()
        PackInkPresets.register()
    }

    // MARK: The catalog

    /// The page appeared, or Check the catalog again: one fetch, no polling.
    func refreshCatalog() async {
        guard !checking else { return }
        checking = true
        defer { checking = false }
        let client = self.client ?? makeClient(store)
        self.client = client
        let result = await client.refresh()
        catalogNote = result.error
        if let doc = result.catalog { catalog = doc.packs }
    }

    /// What the catalog row for `entry` offers, given what is installed. Reads `installed`, which the
    /// page observes, so the row changes the moment a pack is installed or removed.
    func offer(for entry: WidgetCatalogEntry) -> Offer {
        guard let row = installed.first(where: { $0.id == entry.id }) else { return .install }
        if row.madeHere { return .madeHere }
        return WidgetCatalog.compareVersions(entry.version, row.version) > 0 ? .update : .installed
    }

    // MARK: Install, update, uninstall

    func install(_ entry: WidgetCatalogEntry) async {
        guard !busy.contains(entry.id) else { return }
        busy.insert(entry.id)
        defer { busy.remove(entry.id) }
        notice = nil
        let client = self.client ?? makeClient(store)
        self.client = client
        switch await client.download(entry) {
        case .failure(let e):
            notice = e.message
        case .success(let pack):
            let r = store.install(pack.files, pack.manifest, source: .catalog)
            notice = r.error
            packsChanged(posting: r.error == nil)
        }
    }

    func uninstall(_ id: String) {
        guard !busy.contains(id) else { return }
        let r = store.uninstall(id)
        notice = r.error
        packsChanged(posting: r.error == nil)
    }

    /// `posting`: the installed packs really changed, so the Desk's companion reloads its pack now.
    private func packsChanged(posting: Bool) {
        reloadInstalled()
        changed()
        if posting { NotificationCenter.default.post(name: .widgetPacksDidChange, object: self) }
    }

    func reloadInstalled() {
        installed = store.load().packs.compactMap { row in
            guard let info = store.readInfo(row.id) else { return nil }
            return InstalledRow(id: row.id, name: info.name, kind: info.kind, version: row.version, madeHere: row.madeHere)
        }
        companion = CompanionSettings.load(defaults)
    }

    // MARK: The companion

    /// The installed sprite packs, by id: the companion picker's choices.
    var sprites: [InstalledRow] { installed.filter { $0.kind == "sprite" } }

    /// The pack the companion draws now: the pick while it is an installed sprite, else the first.
    var activeCompanion: String? {
        companion.activePack(store.load()) { [store] in store.readInfo($0)?.kind }
    }

    /// Picks the companion (nil: the first installed sprite).
    func pickCompanion(_ id: String?) {
        companion.pack = id
        companion.save(defaults)
    }

    // MARK: Use my own art

    /// One stage's file dialog: the stage id and the sentence the dialog is titled with.
    struct OwnArtPrompt: Equatable {
        let stageId: String
        let title: String
    }

    /// "Use my own art..." is offered on a sprite made here (Claude's or a local one), never on a catalog pack.
    func canUseOwnArt(_ row: InstalledRow) -> Bool {
        guard row.madeHere, row.kind == "sprite", let entry = store.load().find(row.id) else { return false }
        return entry.source != .catalog
    }

    /// The first installed row that offers own art: it carries the search anchor, so one anchor names one place.
    var firstOwnArtRow: InstalledRow? { installed.first { canUseOwnArt($0) } }

    /// The dialogs to show, one per stage, in the sprite's order (Windows' titles). Nil, with the status set, when the
    /// pack can't be read or isn't a made-here sprite.
    func ownArtPrompts(for id: String) -> [OwnArtPrompt]? {
        ownArtStatus = nil
        let name = installed.first { $0.id == id }?.name ?? id
        guard let row = installed.first(where: { $0.id == id }), canUseOwnArt(row),
              let files = store.readFiles(id), let sprite = WidgetOwnArt.sprite(files) else {
            ownArtStatus = "\(name) couldn't be read, so it is unchanged."
            return nil
        }
        let count = sprite.stages.count
        return sprite.stages.enumerated().map { i, stage in
            let cells = "\(stage.frameWidth)x\(stage.frameHeight) pixel cells"
            let title = count == 1
                ? "Your own art for \(name) (\(cells))"
                : "Your own art for \(name): stage \(i + 1) of \(count), \(stage.name) (\(cells))"
            return OwnArtPrompt(stageId: stage.id, title: title)
        }
    }

    /// One picked file against the pack image rules, before it is kept: nil when it may be used, else the sentence
    /// (also left in `ownArtStatus`). `data` nil: the file couldn't be read.
    func checkOwnArt(id: String, stageId: String, data: Data?) -> String? {
        let name = installed.first { $0.id == id }?.name ?? id
        var why: String?
        if let data {
            if let files = store.readFiles(id), let sprite = WidgetOwnArt.sprite(files),
               let stage = sprite.stages.first(where: { $0.id == stageId }) {
                why = WidgetOwnArt.checkImage(data, stage: stage, maxFrame: sprite.maxFrame)
                    .map { "\(stage.name): \($0) \(name) is unchanged." }
            } else {
                why = "\(name) couldn't be read, so it is unchanged."
            }
        } else {
            why = "That image couldn't be read, so \(name) is unchanged."
        }
        ownArtStatus = why
        return why
    }

    /// The chosen files' bytes (stage id to PNG) put into the pack, the whole pack checked again, and the result
    /// installed as the user's own (`.local`, `replaceMadeHere`) with the patch version bumped. A stage with no file
    /// keeps its sheet. True when it was installed. Nothing leaves this Mac.
    @discardableResult
    func useOwnArt(id: String, art: [String: Data]) -> Bool {
        guard !busy.contains(id) else { return false }
        let name = installed.first { $0.id == id }?.name ?? id
        ownArtStatus = nil
        guard let row = installed.first(where: { $0.id == id }), canUseOwnArt(row), let files = store.readFiles(id),
              let sprite = WidgetOwnArt.sprite(files) else {
            ownArtStatus = "\(name) couldn't be read, so it is unchanged."
            return false
        }
        if art.isEmpty {
            ownArtStatus = "No image was chosen, so \(name) is unchanged."
            return false
        }
        for stage in sprite.stages {
            if let data = art[stage.id], let why = WidgetOwnArt.checkImage(data, stage: stage, maxFrame: sprite.maxFrame) {
                ownArtStatus = "\(stage.name): \(why) \(name) is unchanged."
                return false
            }
        }
        busy.insert(id)
        defer { busy.remove(id) }
        let made = WidgetOwnArt.apply(files, packId: id, art: art)
        guard let newFiles = made.files, let manifest = made.manifest else {
            ownArtStatus = "\(name) is unchanged: \(made.errors.first ?? "the pack didn't pass its checks.")"
            return false
        }
        let r = store.install(newFiles, manifest, source: .local, replaceMadeHere: true)
        ownArtStatus = r.ok ? "\(name) now uses your own art. It stays on this Mac." : (r.error ?? "\(name) couldn't be changed.")
        packsChanged(posting: r.ok)
        return r.ok
    }

    /// Effects packs are listed as installed only.
    func drawsNothing(_ row: InstalledRow) -> Bool { row.kind == "effects" }
}

extension Notification.Name {
    /// A pack was installed, updated or uninstalled; the companion re-reads which pack it draws.
    static let widgetPacksDidChange = Notification.Name("sanduhrWidgetPacksDidChange")
}
