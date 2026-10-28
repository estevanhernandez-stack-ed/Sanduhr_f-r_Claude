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

    let store: WidgetStore
    private let defaults: UserDefaults
    private let makeClient: (WidgetStore) -> WidgetCatalogClient
    private let changed: () -> Void
    private var client: WidgetCatalogClient?

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

    /// Effects packs are listed as installed only.
    func drawsNothing(_ row: InstalledRow) -> Bool { row.kind == "effects" }
}

extension Notification.Name {
    /// A pack was installed, updated or uninstalled; the companion re-reads which pack it draws.
    static let widgetPacksDidChange = Notification.Name("sanduhrWidgetPacksDidChange")
}
