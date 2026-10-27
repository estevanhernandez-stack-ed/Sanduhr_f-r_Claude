import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// A temp folder per test, removed when it goes out of scope.
final class WidgetTestRoot {
    let url = FileManager.default.temporaryDirectory
        .appendingPathComponent("sanduhr-widgets-" + UUID().uuidString.prefix(10), isDirectory: true)
    deinit { try? FileManager.default.removeItem(at: url) }

    var widgets: URL { url.appendingPathComponent("widgets", isDirectory: true) }

    static let clock = Date(timeIntervalSince1970: 1_792_670_400)   // 2026-10-22T12:00:00Z

    func store(encodePng: ((PixelSheetImage) throws -> Data)? = nil) -> WidgetStore {
        WidgetStore(root: widgets, encodePng: encodePng, clock: { Self.clock })
    }
}

enum StoreFixtures {
    static func valid(_ group: String, _ name: String) -> (files: [String: Data], manifest: PackManifest) {
        let files = WidgetPackFixtures.load(group, name)
        let v = PackValidator.validate(files, folderName: name, strict: true)
        #expect(v.ok, "\(v.errors.joined(separator: " | "))")
        return (files, v.manifest!)
    }

    static func withVersion(_ files: [String: Data], _ version: String) -> [String: Data] {
        var copy = files
        let text = String(decoding: files["pack.json"]!, as: UTF8.self).replacingOccurrences(of: "\"1.0.0\"", with: "\"\(version)\"")
        copy["pack.json"] = Data(text.utf8)
        return copy
    }

    static func relativeFiles(in dir: URL) -> [String] {
        let base = dir.standardizedFileURL.path + "/"
        guard let walker = FileManager.default.enumerator(at: dir, includingPropertiesForKeys: [.isRegularFileKey]) else { return [] }
        var out: [String] = []
        for case let url as URL in walker where (try? url.resourceValues(forKeys: [.isRegularFileKey]))?.isRegularFile == true {
            out.append(String(url.standardizedFileURL.path.dropFirst(base.count)))
        }
        return out.sorted()
    }

    static func stagingIsEmpty(_ store: WidgetStore) -> Bool {
        ((try? FileManager.default.contentsOfDirectory(atPath: store.stagingDir.path)) ?? []).isEmpty
    }
}

struct DiskFull: Error {}

/// FORMAT 7 rule 6 and spec 2: the installed packs on disk, widgets.json, atomic install, uninstall
/// and the rollback marker. A port of Windows' WidgetStoreTests.cs.
@Suite("Widget store")
struct WidgetStoreTests {
    @Test func installWritesTheFolderAndTheRow() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        let r = store.install(files, manifest, source: .catalog)
        #expect(r.ok, "\(r.error ?? "")")

        let dir = store.packDir("good-palette")
        #expect(StoreFixtures.relativeFiles(in: dir) == files.keys.sorted())
        #expect(try Data(contentsOf: dir.appendingPathComponent("palettes.json")) == files["palettes.json"])

        let rows = store.load().packs
        #expect(rows.count == 1)
        #expect(rows[0].id == "good-palette" && rows[0].version == "1.0.0" && rows[0].source == .catalog && rows[0].enabled)
        #expect(WidgetRegistry.stamp(rows[0].installedAt) == "2026-10-22T12:00:00Z")
        #expect(StoreFixtures.stagingIsEmpty(store))

        let json = try String(contentsOf: store.registryFile, encoding: .utf8)
        #expect(json.contains("\"source\": \"catalog\""))
        #expect(json.contains("\"installed_at\": \"2026-10-22T12:00:00Z\""))
        #expect(json.contains("\"schema_version\": 1"))

        let info = store.readInfo("good-palette")
        #expect(info?.name == "Fixture palette" && info?.kind == "palette")
    }

    @Test func pixelSheetsAreRasterizedToPngAtInstall() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-sprite-pixel")
        #expect(store.install(files, manifest, source: .catalog).ok)
        let sheets = WidgetStore.pixelSheets(of: files)
        #expect(!sheets.isEmpty)
        for sheet in sheets {
            let png = try Data(contentsOf: store.rasterPath("good-sprite-pixel", sheet))
            if case .failure(let e) = PackValidator.parsePng([UInt8](png)) { Issue.record("the raster is not a PNG: \(e)") }
        }
    }

    @Test func stagesMixingAPixelSheetAndAPngRasterizeOnlyThePixelSheet() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-sprite-pixel-stages")
        #expect(store.install(files, manifest, source: .catalog).ok)
        #expect(WidgetStore.pixelSheets(of: files) == ["sprites/one.json"])
        let dir = store.packDir("good-sprite-pixel-stages")
        #expect(FileManager.default.fileExists(atPath: store.rasterPath("good-sprite-pixel-stages", "sprites/one.json").path))
        #expect(FileManager.default.fileExists(atPath: dir.appendingPathComponent("sprites/two.png").path))
        #expect(StoreFixtures.relativeFiles(in: dir.appendingPathComponent(WidgetStore.rasterName)) == ["sprites/one.json.png"])
    }

    @Test func anUpdateFromTheSameSourceReplacesThePack() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        let stale = store.packDir("good-palette").appendingPathComponent("stale.txt")
        try Data("x".utf8).write(to: stale)

        let next = StoreFixtures.withVersion(files, "1.1.0")
        let v = PackValidator.validate(next, folderName: "good-palette", strict: true)
        #expect(store.install(next, v.manifest!, source: .catalog).ok)
        #expect(store.load().find("good-palette")?.version == "1.1.0")
        #expect(!FileManager.default.fileExists(atPath: stale.path))
    }

    @Test func anIdInstalledFromAnotherSourceIsRefused() {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .claude).ok)
        let r = store.install(files, manifest, source: .catalog)
        #expect(!r.ok)
        #expect(r.error?.contains("made here") == true)
        #expect(store.load().find("good-palette")?.source == .claude)

        // Made-here sources may change hands when asked; a catalog pack never does.
        #expect(store.install(files, manifest, source: .local).ok == false)
        #expect(store.install(files, manifest, source: .local, replaceMadeHere: true).ok)
        #expect(store.load().find("good-palette")?.source == .local)
        #expect(store.install(files, manifest, source: .catalog, replaceMadeHere: true).ok == false)
    }

    @Test func aFailureBeforeTheSwapLeavesTheOldInstallUntouched() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        let before = try Data(contentsOf: store.registryFile)

        let next = StoreFixtures.withVersion(files, "1.1.0")
        store.beforeSwap = { throw DiskFull() }
        let r = store.install(next, PackValidator.validate(next, folderName: "good-palette", strict: true).manifest!, source: .catalog)
        #expect(!r.ok)
        #expect(try Data(contentsOf: store.registryFile) == before)
        let packJson = try String(contentsOf: store.packDir("good-palette").appendingPathComponent("pack.json"), encoding: .utf8)
        #expect(packJson.contains("\"1.0.0\""))
        #expect(StoreFixtures.stagingIsEmpty(store))
    }

    @Test func aFailureAfterTheSwapPutsTheOldInstallBack() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        let before = try Data(contentsOf: store.registryFile)

        let next = StoreFixtures.withVersion(files, "1.1.0")
        store.beforeRegistryWrite = { throw DiskFull() }
        let r = store.install(next, PackValidator.validate(next, folderName: "good-palette", strict: true).manifest!, source: .catalog)
        #expect(!r.ok)
        #expect(r.error?.contains("/") == false)   // no path in the sentence
        #expect(try Data(contentsOf: store.registryFile) == before)
        let packJson = try String(contentsOf: store.packDir("good-palette").appendingPathComponent("pack.json"), encoding: .utf8)
        #expect(packJson.contains("\"1.0.0\""))
        #expect(StoreFixtures.stagingIsEmpty(store))
    }

    @Test func aFailedFirstInstallLeavesNothing() {
        let root = WidgetTestRoot()
        let store = root.store(encodePng: { _ in throw DiskFull() })
        let (files, manifest) = StoreFixtures.valid("good", "good-sprite-pixel")
        let r = store.install(files, manifest, source: .catalog)
        #expect(!r.ok)
        #expect(!FileManager.default.fileExists(atPath: store.packDir("good-sprite-pixel").path))
        #expect(store.load().packs.isEmpty)
        #expect(StoreFixtures.stagingIsEmpty(store))
    }

    @Test func uninstallRemovesTheFolderAndTheRow() {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        #expect(store.uninstall("good-palette").ok)
        #expect(!FileManager.default.fileExists(atPath: store.packDir("good-palette").path))
        #expect(store.load().packs.isEmpty)
        #expect(!store.uninstall("good-palette").ok)
        #expect(!store.uninstall("../escape").ok)
    }

    @Test func installedOfKindReturnsOnlyThatKindOrderedById() {
        let root = WidgetTestRoot()
        let store = root.store()
        for name in ["good-theme", "good-palette", "good-font"] {
            let (files, manifest) = StoreFixtures.valid("good", name)
            #expect(store.install(files, manifest, source: .catalog).ok)
        }
        let palettes = store.installed(ofKind: "palette")
        #expect(palettes.map(\.row.id) == ["good-palette"])
        #expect(palettes.first?.info.name == "Fixture palette")
        #expect(palettes.first?.dir == store.packDir("good-palette"))
        #expect(store.installed(ofKind: "theme").map(\.row.id) == ["good-theme"])
        #expect(store.installed(ofKind: "effects").isEmpty)
    }

    @Test func installedOfKindSkipsADisabledPack() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        #expect(store.installed(ofKind: "palette").count == 1)

        let text = try String(contentsOf: store.registryFile, encoding: .utf8).replacingOccurrences(of: "\"enabled\": true", with: "\"enabled\": false")
        try Data(text.utf8).write(to: store.registryFile)
        #expect(store.load().packs.map(\.enabled) == [false])
        #expect(store.installed(ofKind: "palette").isEmpty)
    }

    @Test func installedOfKindSkipsAPackWithAMissingFolderOrABrokenManifest() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        for name in ["good-palette", "good-theme"] {
            let (files, manifest) = StoreFixtures.valid("good", name)
            #expect(store.install(files, manifest, source: .catalog).ok)
        }
        // A malformed pack.json reads as no pack, a deleted folder too; the other kind is untouched.
        try Data("{ not json".utf8).write(to: store.packDir("good-palette").appendingPathComponent("pack.json"))
        #expect(store.installed(ofKind: "palette").isEmpty)
        try FileManager.default.removeItem(at: store.packDir("good-theme"))
        #expect(store.installed(ofKind: "theme").isEmpty)
    }

    @Test func installedOfKindIncludesAClaudeMadePack() {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .claude).ok)
        #expect(store.installed(ofKind: "palette").first?.row.source == .claude)
    }

    @Test func readFilesReturnsThePacksOwnFilesAndNotTheRaster() {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-sprite-pixel")
        #expect(store.install(files, manifest, source: .catalog).ok)
        #expect(store.readFiles("good-sprite-pixel") == files)
        #expect(store.readFiles("not-installed") == nil)
        #expect(store.readFiles("../escape") == nil)
    }

    @Test func theRollbackMarkerOnlyMovesForwardAndSurvivesInstalls() {
        let root = WidgetTestRoot()
        let store = root.store()
        #expect(store.newestCatalogSeen() == nil)
        store.rememberCatalog("2026-10-22T02:23:13Z")
        store.rememberCatalog("2026-10-01T00:00:00Z")
        #expect(store.newestCatalogSeen() == WidgetCatalog.parseStamp("2026-10-22T02:23:13Z"))
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        #expect(store.load().catalogSeen == "2026-10-22T02:23:13Z")
    }

    @Test func theRegistryReadsLeniently() {
        #expect(WidgetRegistry.parse(Data("not json".utf8)).packs.isEmpty)
        #expect(WidgetRegistry.parse(nil).packs.isEmpty)
        let reg = WidgetRegistry.parse(Data("""
            {"schema_version":1,"catalog_generated_at":"2026-10-22T02:23:13Z","packs":[
              {"id":"good-one","version":"1.0.0","source":"local","installed_at":"2026-10-22T00:00:00Z","enabled":false},
              {"id":"../bad","version":"1.0.0","source":"catalog","installed_at":"2026-10-22T00:00:00Z"},
              {"id":"no-source","version":"1.0.0","installed_at":"2026-10-22T00:00:00Z"}
            ]}
            """.utf8))
        #expect(reg.packs.count == 1)
        let row = reg.packs[0]
        #expect(row.id == "good-one" && row.source == .local && !row.enabled && row.madeHere)
        let back = WidgetRegistry.parse(reg.json())
        #expect(back == reg)
    }

    @Test func anEmptyRegistryRoundTripsAndNeverHoldsABadStamp() {
        let json = WidgetRegistry.empty.json()
        #expect(String(decoding: json, as: UTF8.self).contains("\"catalog_generated_at\": null"))
        #expect(WidgetRegistry.parse(json) == .empty)
        let bad = WidgetRegistry.parse(Data("{\"catalog_generated_at\":\"yesterday\",\"packs\":[]}".utf8))
        #expect(bad.catalogSeen == nil)
    }

    @Test func idsThatAreNotFolderSafeAreRefused() {
        for id in ["ab", "Good-One", "-lead", "trail-", "dou--ble", "with space", "../x", String(repeating: "a", count: 41)] {
            #expect(!WidgetStore.isSafeId(id), "\(id)")
        }
        for id in ["abc", "good-palette", "my-sprite-2", String(repeating: "a", count: 40)] {
            #expect(WidgetStore.isSafeId(id), "\(id)")
        }
    }

    @Test func afterResetNothingIsInstalledRemovedOrRemembered() {
        let root = WidgetTestRoot()
        let store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        ResetGate.$testOverride.withValue(true) {
            #expect(!store.install(files, manifest, source: .catalog).ok)
            #expect(!store.uninstall("good-palette").ok)
            store.rememberCatalog("2026-10-22T02:23:13Z")
            store.cleanStaging()
        }
        #expect(store.load().catalogSeen == nil)
        #expect(store.load().packs.count == 1)

        let fresh = WidgetTestRoot().store()
        ResetGate.$testOverride.withValue(true) {
            #expect(!fresh.install(files, manifest, source: .catalog).ok)
        }
        #expect(!FileManager.default.fileExists(atPath: fresh.root.path))
    }

    @Test func theStandardRootIsTheWidgetsFolderInSanduhrsSupportFolder() {
        #expect(WidgetStore.standardRoot.lastPathComponent == "widgets")
        #expect(WidgetStore.standardRoot.deletingLastPathComponent().lastPathComponent == "Sanduhr")
    }
}

/// The client end to end over a dictionary of bytes (no network), installing through the store.
@Suite("Widget store with the catalog client")
struct WidgetStoreClientTests {
    private let base = "https://test.invalid/"

    private struct Published {
        let files: [String: Data]
        let key: P256.Signing.PrivateKey
        let entry: WidgetCatalogEntry
        let zip: Data
    }

    private func published(_ generatedAt: String = "2026-10-22T02:23:13Z", min: String = "3.8.0") -> Published {
        let key = P256.Signing.PrivateKey()
        let zip = WidgetTestKit.zipPack(WidgetPackFixtures.load("good", "good-palette"))
        let entry = CatalogKit.entry("good-palette", "Fixture palette", "palette", "1.0.0", [UInt8](zip), min: min)
        let catalog = CatalogKit.catalog(generatedAt, [entry])
        let files: [String: Data] = [
            base + "catalog.json": Data(catalog), base + "catalog.json.sig": Data(CatalogKit.sigFile(key, catalog)),
            entry.url: zip,
        ]
        return Published(files: files, key: key, entry: entry, zip: zip)
    }

    private func client(_ p: Published, _ store: WidgetStore, supported: String = PackFormat.supported) -> WidgetCatalogClient {
        WidgetCatalogClient(fetch: { url, max in
            guard let d = p.files[url] else { return nil }
            if d.count > max { throw WidgetFetchError(message: "too large") }
            return d
        }, marker: store, pinnedKeys: [CatalogKit.spki(p.key)], baseUrl: base, supported: supported)
    }

    @Test func refreshThenInstallRunsEveryRuleAndInstalls() async {
        let root = WidgetTestRoot()
        let store = root.store()
        let p = published()
        let c = client(p, store)
        let refresh = await c.refresh()
        #expect(refresh.error == nil)
        #expect(refresh.catalog?.packs == [p.entry])

        switch await c.download(p.entry) {
        case .failure(let e): Issue.record("\(e.message)")
        case .success(let pack):
            let r = store.install(pack.files, pack.manifest, source: .catalog)
            #expect(r.ok, "\(r.error ?? "")")
        }
        #expect(store.load().find("good-palette")?.source == .catalog)
        #expect(store.load().catalogSeen == "2026-10-22T02:23:13Z")
    }

    @Test func aBadSignatureIsNothingParsedOrRemembered() async {
        let root = WidgetTestRoot()
        let store = root.store()
        var p = published()
        var cat = p.files[base + "catalog.json"]!
        cat.append(contentsOf: [0x20, 0x0A])
        var files = p.files
        files[base + "catalog.json"] = cat
        p = Published(files: files, key: p.key, entry: p.entry, zip: p.zip)
        let c = client(p, store)
        #expect(await c.refresh().error == WidgetCatalogClient.unverified)
        #expect(c.current == nil)
        #expect(store.newestCatalogSeen() == nil)
    }

    @Test func anOlderCatalogAfterANewerOneIsARollbackTheStoreRemembers() async {
        let root = WidgetTestRoot()
        let store = root.store()
        let newer = published("2026-10-22T02:23:13Z")
        #expect(await client(newer, store).refresh().error == nil)
        let older = published("2026-10-21T00:00:00Z")
        #expect(await client(older, store).refresh().error == WidgetCatalogClient.rolledBack)
        #expect(store.load().catalogSeen == "2026-10-22T02:23:13Z")
    }

    @Test func aDownloadThatDoesNotMatchTheCatalogInstallsNothing() async {
        let root = WidgetTestRoot()
        let store = root.store()
        var p = published()
        var other = [UInt8](p.zip)
        other[other.count - 30] ^= 1   // same size, different hash
        var files = p.files
        files[p.entry.url] = Data(other)
        p = Published(files: files, key: p.key, entry: p.entry, zip: p.zip)
        let c = client(p, store)
        _ = await c.refresh()
        if case .failure(let e) = await c.download(p.entry) { #expect(e.message == WidgetCatalogClient.downloadMismatch) } else { Issue.record("installed a mismatch") }
        #expect(store.load().packs.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: store.packDir("good-palette").path))
    }

    @Test func aPackThatFailsValidationIsRefusedBeforeTheStoreSeesIt() async {
        let root = WidgetTestRoot()
        let store = root.store()
        let key = P256.Signing.PrivateKey()
        let badZip = WidgetTestKit.zipPack(WidgetPackFixtures.load("bad", "bad-unlisted-file"))
        let bad = CatalogKit.entry("bad-unlisted-file", "Bad", "palette", "1.0.0", [UInt8](badZip))
        let catalog = CatalogKit.catalog("2026-10-22T02:23:13Z", [bad])
        let files: [String: Data] = [base + "catalog.json": Data(catalog), base + "catalog.json.sig": Data(CatalogKit.sigFile(key, catalog)),
                                     bad.url: badZip]
        let p = Published(files: files, key: key, entry: bad, zip: badZip)
        let c = client(p, store)
        #expect(await c.refresh().error == nil)
        if case .failure(let e) = await c.download(bad) { #expect(e.message.contains("didn't pass Sanduhr's checks")) } else { Issue.record("installed a bad pack") }
        #expect(store.load().packs.isEmpty)
    }
}
