import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// A defaults suite that is removed with the test (a scratch domain left behind leaves a plist).
final class WidgetScratchDefaults {
    let name = "sanduhr.tests.widgets.\(UUID().uuidString)"
    let defaults: UserDefaults
    init() { defaults = UserDefaults(suiteName: name)! }
    deinit { defaults.removePersistentDomain(forName: name) }
}

/// Pack themes and palettes join the lists, and leave with the pack.
@Suite("Pack themes and palettes")
struct PackListsTests {
    @Test func aThemePacksThemesJoinWithThePacksIdAndLeaveOnUninstall() {
        let root = WidgetTestRoot()
        let store = root.store()
        #expect(PackThemes.installed(in: store).isEmpty)
        let (files, manifest) = StoreFixtures.valid("good", "good-theme")
        #expect(store.install(files, manifest, source: .catalog).ok)

        let themes = PackThemes.installed(in: store)
        #expect(themes.map(\.id) == ["good-theme:fixture-dark"])
        #expect(themes.first?.displayName == "Fixture Dark")
        // The colon keeps a pack theme apart from every built-in and every user theme file.
        #expect(!ThemeRegistry.builtIn.contains { $0.id == themes[0].id })

        #expect(store.uninstall("good-theme").ok)
        #expect(PackThemes.installed(in: store).isEmpty)
    }

    @Test func aDisabledOrWrongKindPackAddsNoThemes() {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        #expect(PackThemes.installed(in: store).isEmpty)
    }

    @Test func aThemeFileThePackCantLoadIsSkipped() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-theme")
        #expect(store.install(files, manifest, source: .catalog).ok)
        let themes = store.packDir("good-theme").appendingPathComponent("themes")
        try Data("{ nope".utf8).write(to: themes.appendingPathComponent("broken.json"))
        #expect(PackThemes.installed(in: store).map(\.id) == ["good-theme:fixture-dark"])
    }

    @Test func aPalettePacksPalettesJoinNamedAsInThePackAndLeaveOnUninstall() {
        let root = WidgetTestRoot()
        let store = root.store()
        #expect(PackInkPresets.palettes(in: store).isEmpty)
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)

        let palettes = PackInkPresets.palettes(in: store)
        #expect(palettes.map(\.name) == ["plain \u{00B7} Fixture palette"])
        #expect(palettes.first?.colors == ["ff2a6d", "05d9e8"])
        // Built-ins first, the pack's after, and no name twice.
        let all = MessagePalette.builtIn + palettes
        #expect(Set(all.map(\.name)).count == all.count)
        #expect(all.prefix(MessagePalette.builtIn.count).map(\.name) == MessagePalette.builtIn.map(\.name))

        #expect(store.uninstall("good-palette").ok)
        #expect(PackInkPresets.palettes(in: store).isEmpty)
    }

    @Test func hexStopsNormalizeAndAnythingElseIsDropped() {
        #expect(PackInkPresets.normalize("#FF2A6D") == "ff2a6d")
        #expect(PackInkPresets.normalize("05d9e8") == "05d9e8")
        for bad in ["#fff", "ff2a6", "gggggg", "#ff2a6dff", ""] { #expect(PackInkPresets.normalize(bad) == nil, "\(bad)") }
    }

    @Test func seededNowPlayingLooksDrawOnlyFromTheBuiltIns() {
        // Installing a palette pack must not change the look a song already has.
        let key = NowPlayingLooks.songKey(artist: "a", title: "b")
        #expect(MessagePalette.builtIn.map(\.colors).contains(NowPlayingLooks.seeded(key).colors))
    }
}

/// The Companion pick rule and its two stored choices.
@Suite("Companion settings")
struct CompanionSettingsTests {
    private func registry(_ ids: [String]) -> WidgetRegistry {
        WidgetRegistry(catalogSeen: nil, packs: ids.map {
            InstalledWidget(id: $0, version: "1.0.0", source: .catalog, installedAt: WidgetTestRoot.clock, enabled: true)
        })
    }

    @Test func noPickUsesTheFirstInstalledSpriteById() {
        let kinds = ["zeta-sprite": "sprite", "alpha-sprite": "sprite", "a-theme": "theme"]
        let reg = registry(["zeta-sprite", "a-theme", "alpha-sprite"])
        let s = CompanionSettings()
        #expect(s.activePack(reg) { kinds[$0] } == "alpha-sprite")
        #expect(CompanionSettings(pack: "zeta-sprite").activePack(reg) { kinds[$0] } == "zeta-sprite")
    }

    @Test func aPickThatIsGoneOrNotASpriteFallsBack() {
        let kinds = ["alpha-sprite": "sprite", "a-theme": "theme"]
        let reg = registry(["alpha-sprite", "a-theme"])
        #expect(CompanionSettings(pack: "a-theme").activePack(reg) { kinds[$0] } == "alpha-sprite")
        #expect(CompanionSettings(pack: "removed-one").activePack(reg) { kinds[$0] } == "alpha-sprite")
        #expect(CompanionSettings().activePack(registry([])) { _ in nil } == nil)
    }

    @Test func absentReadsAsNoPickAndTheKeyIsTheDesksOwn() {
        let scratch = WidgetScratchDefaults()
        #expect(CompanionSettings.load(scratch.defaults) == CompanionSettings(pack: nil))
        CompanionSettings(pack: "alpha-sprite").save(scratch.defaults)
        #expect(scratch.defaults.string(forKey: "deskCompanionPack") == "alpha-sprite")
        #expect(CompanionSettings.load(scratch.defaults) == CompanionSettings(pack: "alpha-sprite"))
        CompanionSettings(pack: nil).save(scratch.defaults)
        #expect(scratch.defaults.object(forKey: "deskCompanionPack") == nil)
    }

    @Test func aStoredPackThatIsNotAnIdReadsAsNone() {
        let scratch = WidgetScratchDefaults()
        scratch.defaults.set("../escape", forKey: "deskCompanionPack")
        #expect(CompanionSettings.load(scratch.defaults).pack == nil)
    }

    @Test func nothingIsWrittenAfterReset() {
        let scratch = WidgetScratchDefaults()
        ResetGate.$testOverride.withValue(true) {
            CompanionSettings(pack: "alpha-sprite").save(scratch.defaults)
        }
        #expect(scratch.defaults.object(forKey: "deskCompanionPack") == nil)
    }
}

/// The page's model: catalog on request, install, update, uninstall, the Companion.
@MainActor
@Suite("Widgets page model")
struct WidgetsModelTests {
    private let base = "https://test.invalid/"

    private struct Net {
        var files: [String: Data]
        var asked: [String] = []
        let key: P256.Signing.PrivateKey
        let entry: WidgetCatalogEntry
    }

    private final class Box { var net: Net; init(_ n: Net) { net = n } }

    private func publish(_ version: String = "1.0.0", generatedAt: String = "2026-10-22T02:23:13Z",
                         kind: String = "palette", pack: String = "good-palette",
                         key: P256.Signing.PrivateKey = P256.Signing.PrivateKey()) -> Net {
        let files = WidgetPackFixtures.load("good", pack)
        let zip = WidgetTestKit.zipPack(StoreFixtures.withVersion(files, version))
        let entry = CatalogKit.entry(pack, "Fixture", kind, version, [UInt8](zip))
        let catalog = CatalogKit.catalog(generatedAt, [entry])
        return Net(files: [base + "catalog.json": Data(catalog), base + "catalog.json.sig": Data(CatalogKit.sigFile(key, catalog)),
                           entry.url: zip], key: key, entry: entry)
    }

    private func model(_ box: Box, _ root: WidgetTestRoot, _ scratch: WidgetScratchDefaults,
                       changed: @escaping () -> Void = {}) -> WidgetsModel {
        WidgetsModel(store: root.store(), defaults: scratch.defaults, makeClient: { [base] store in
            WidgetCatalogClient(fetch: { url, _ in
                box.net.asked.append(url)
                if url.hasPrefix("https://offline") { throw WidgetFetchError(message: "down") }
                return box.net.files[url]
            }, marker: store, pinnedKeys: [CatalogKit.spki(box.net.key)], baseUrl: base)
        }, changed: changed)
    }

    @Test func nothingIsFetchedUntilTheCatalogIsAsked() async {
        let box = Box(publish())
        let m = model(box, WidgetTestRoot(), WidgetScratchDefaults())
        #expect(box.net.asked.isEmpty && m.catalog.isEmpty)
        await m.refreshCatalog()
        #expect(m.catalog == [box.net.entry] && m.catalogNote == nil)
        #expect(box.net.asked.contains(base + "catalog.json"))
        let asks = box.net.asked.count
        await m.refreshCatalog()
        #expect(box.net.asked.count > asks)   // only because it was asked again
    }

    @Test func aFailedCheckIsOneQuietLineAndKeepsTheLastCatalog() async {
        let box = Box(publish())
        let m = model(box, WidgetTestRoot(), WidgetScratchDefaults())
        await m.refreshCatalog()
        box.net.files[base + "catalog.json"] = nil
        await m.refreshCatalog()
        #expect(m.catalogNote == WidgetCatalogClient.offline)
        #expect(m.catalog == [box.net.entry])
        #expect(!m.checking)
    }

    @Test func theCatalogRowFollowsAnUninstallAtOnce() async {
        let box = Box(publish(kind: "sprite", pack: "good-sprite"))
        let m = model(box, WidgetTestRoot(), WidgetScratchDefaults())
        await m.refreshCatalog()
        await m.install(box.net.entry)
        #expect(m.offer(for: box.net.entry) == .installed)
        m.uninstall("good-sprite")
        // The row reads the observed list, so it offers Install again without a reload.
        #expect(m.offer(for: box.net.entry) == .install)
    }

    @Test func installUpdateAndUninstallWalkThroughTheOffers() async {
        let root = WidgetTestRoot()
        var changes = 0
        let box = Box(publish("1.0.0"))
        let m = model(box, root, WidgetScratchDefaults(), changed: { changes += 1 })
        await m.refreshCatalog()
        #expect(m.offer(for: box.net.entry) == .install)

        await m.install(box.net.entry)
        #expect(m.notice == nil && changes == 1)
        #expect(m.offer(for: box.net.entry) == .installed)
        #expect(m.installed.map(\.id) == ["good-palette"])
        #expect(m.installed.first?.sourceText == "from the catalog" && m.installed.first?.kind == "palette")
        #expect(m.installed.first?.version == "1.0.0")

        // A newer catalog offers Update; installing it replaces the pack.
        let newer = publish("1.1.0", generatedAt: "2026-10-23T00:00:00Z", key: box.net.key)
        box.net = newer
        await m.refreshCatalog()
        #expect(m.offer(for: newer.entry) == .update)
        await m.install(newer.entry)
        #expect(m.offer(for: newer.entry) == .installed && m.installed.first?.version == "1.1.0" && changes == 2)

        m.uninstall("good-palette")
        #expect(m.installed.isEmpty && changes == 3)
        #expect(m.offer(for: newer.entry) == .install)
    }

    @Test func theCompanionIsToldWhenPacksChangeAndOnlyThen() async {
        var posts = 0
        let box = Box(publish("1.0.0"))
        let m = model(box, WidgetTestRoot(), WidgetScratchDefaults())
        // Scoped to this model: other suites install packs in parallel.
        let token = NotificationCenter.default.addObserver(forName: .widgetPacksDidChange, object: m, queue: nil) { _ in posts += 1 }
        defer { NotificationCenter.default.removeObserver(token) }
        await m.refreshCatalog()
        #expect(posts == 0)
        await m.install(box.net.entry)
        #expect(posts == 1)
        m.uninstall("good-palette")
        #expect(posts == 2)
        m.uninstall("not-installed")
        #expect(posts == 2)
    }

    @Test func aPackMadeHereIsNeverOfferedAsAnInstallOver() async {
        let root = WidgetTestRoot()
        let box = Box(publish())
        let m = model(box, root, WidgetScratchDefaults())
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(m.store.install(files, manifest, source: .claude).ok)
        m.reloadInstalled()
        #expect(m.offer(for: box.net.entry) == .madeHere)
        #expect(m.installed.first?.sourceText == "made here")
        await m.refreshCatalog()
        await m.install(box.net.entry)
        #expect(m.notice?.contains("made here") == true)
        #expect(m.store.load().find("good-palette")?.source == .claude)
    }

    @Test func aFailedDownloadIsASentenceAndInstallsNothing() async {
        var net = publish()
        net.files[net.entry.url] = nil
        let box = Box(net)
        var changes = 0
        let m = model(box, WidgetTestRoot(), WidgetScratchDefaults(), changed: { changes += 1 })
        await m.refreshCatalog()
        await m.install(net.entry)
        #expect(m.notice == WidgetCatalogClient.downloadFailed)
        #expect(m.installed.isEmpty && m.busy.isEmpty && changes == 0)
    }

    @Test func installedThemeAndPaletteListsFollowInstallAndUninstall() async {
        let root = WidgetTestRoot()
        let box = Box(publish())
        let m = model(box, root, WidgetScratchDefaults())
        await m.refreshCatalog()
        await m.install(box.net.entry)
        #expect(PackInkPresets.palettes(in: m.store).count == 1)
        m.uninstall("good-palette")
        #expect(PackInkPresets.palettes(in: m.store).isEmpty)

        let (files, manifest) = StoreFixtures.valid("good", "good-theme")
        #expect(m.store.install(files, manifest, source: .catalog).ok)
        #expect(PackThemes.installed(in: m.store).count == 1)
        m.uninstall("good-theme")
        #expect(PackThemes.installed(in: m.store).isEmpty)
    }

    @Test func theCompanionIsOneAtATimeAndDefaultsToTheFirstSprite() {
        let root = WidgetTestRoot()
        let scratch = WidgetScratchDefaults()
        let m = model(Box(publish()), root, scratch)
        #expect(m.sprites.isEmpty && m.activeCompanion == nil)

        for name in ["good-sprite", "good-sprite-pixel", "good-theme"] {
            let (files, manifest) = StoreFixtures.valid("good", name)
            #expect(m.store.install(files, manifest, source: .catalog).ok)
        }
        m.reloadInstalled()
        #expect(m.sprites.map(\.id) == ["good-sprite", "good-sprite-pixel"])
        #expect(m.activeCompanion == "good-sprite")

        m.pickCompanion("good-sprite-pixel")
        #expect(m.activeCompanion == "good-sprite-pixel")
        #expect(scratch.defaults.string(forKey: "deskCompanionPack") == "good-sprite-pixel")

        // A fresh model reads the pick back; removing it falls back to the first sprite.
        let again = model(Box(publish()), root, scratch)
        #expect(again.companion == CompanionSettings(pack: "good-sprite-pixel"))
        again.uninstall("good-sprite-pixel")
        #expect(again.activeCompanion == "good-sprite")
    }

    @Test func effectsPacksAreListedWithTheNoteAndNothingElse() {
        let root = WidgetTestRoot()
        let m = model(Box(publish()), root, WidgetScratchDefaults())
        let (files, manifest) = StoreFixtures.valid("good", "good-effects")
        #expect(m.store.install(files, manifest, source: .catalog).ok)
        m.reloadInstalled()
        let row = m.installed[0]
        #expect(row.kind == "effects" && m.drawsNothing(row))
        #expect(WidgetsModel.effectsNote == "Effects packs don't draw yet.")
        #expect(m.sprites.isEmpty)
    }
}
