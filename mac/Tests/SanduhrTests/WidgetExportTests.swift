import Foundation
import Testing
@testable import Sanduhr

@Suite("Export a pack (2.43.0)")
@MainActor
struct WidgetExportTests {
    private func model(_ root: WidgetTestRoot, _ scratch: WidgetScratchDefaults) -> WidgetsModel {
        WidgetsModel(store: root.store(), defaults: scratch.defaults,
                     makeClient: { WidgetCatalogClient(fetch: { _, _ in nil }, marker: $0) }, changed: {})
    }

    private func installLocal(_ name: String, in store: WidgetStore) {
        let files = WidgetPackFixtures.load("good", name)
        let outcome = WidgetFileInstall.install(zip: WidgetTestKit.zipPack(files), store: store)
        guard case .installed = outcome else { Issue.record("\(outcome)"); return }
    }

    @Test func aMadeHereSpriteExportsAndInstallsElsewhereByteForByte() throws {
        let root = WidgetTestRoot(), store = root.store()
        installLocal("good-sprite", in: store)
        guard case .zip(let data, let fileName) = WidgetExport.export(id: "good-sprite", store: store) else {
            Issue.record("expected a zip"); return
        }
        let version = try #require(PackValidator.validate(WidgetPackFixtures.load("good", "good-sprite"),
                                                          folderName: "good-sprite", strict: true).manifest).version
        #expect(fileName == "good-sprite-\(version).zip")

        let other = WidgetTestRoot(), fresh = other.store()
        guard case .installed(let m, _) = WidgetFileInstall.install(zip: data, store: fresh) else {
            Issue.record("the exported zip didn't install"); return
        }
        #expect(m.id == "good-sprite")
        #expect(try #require(fresh.readFiles("good-sprite")) == #require(store.readFiles("good-sprite")))
    }

    @Test func onlyALocalPackCanBeExported() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        let store = root.store()
        installLocal("good-sprite", in: store)
        let files = WidgetPackFixtures.load("good", "good-palette")
        let manifest = PackValidator.validate(files, folderName: "good-palette", strict: true).manifest
        #expect(store.install(files, manifest!, source: .catalog).ok)
        let m = WidgetsModel(store: store, defaults: scratch.defaults,
                             makeClient: { WidgetCatalogClient(fetch: { _, _ in nil }, marker: $0) }, changed: {})
        let local = m.installed.first { $0.id == "good-sprite" }!
        let catalog = m.installed.first { $0.id == "good-palette" }!
        #expect(m.canExport(local))
        #expect(!m.canExport(catalog))
        #expect(WidgetExport.canExport(.local) && !WidgetExport.canExport(.catalog) && !WidgetExport.canExport(nil))
        if case .refused = m.exportOutcome(for: "good-palette") {} else { Issue.record("a catalog pack exported") }
    }

    @Test func aPackEditedIntoFailingValidationIsRefusedWithTheValidatorsSentence() throws {
        let root = WidgetTestRoot(), store = root.store()
        installLocal("good-sprite", in: store)
        try FileManager.default.removeItem(at: store.packDir("good-sprite").appendingPathComponent("LICENSE"))
        let outcome = WidgetExport.export(id: "good-sprite", store: store)
        guard case .refused(let why) = outcome else { Issue.record("\(outcome)"); return }
        #expect(why.contains("LICENSE is missing"), "\(why)")
    }

    @Test func moreThanThreeErrorsAreCounted() {
        var files = WidgetPackFixtures.load("good", "good-palette")
        files["LICENSE"] = nil
        for n in 1...4 { files["extra\(n).txt"] = Data("x".utf8) }
        let errors = PackValidator.validate(files, folderName: "good-palette", strict: true).errors
        #expect(errors.count > 3)
        guard case .refused(let why) = WidgetExport.export(files: files, id: "good-palette") else {
            Issue.record("expected a refusal"); return
        }
        #expect(why == WidgetFileInstall.rejected(errors))
        #expect(why.hasSuffix(" more."))
    }

    @Test func theStatusSentencesAndTheWrite() throws {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        let m = model(root, scratch)
        installLocal("good-sprite", in: m.store)
        m.reloadInstalled()
        let name = StoreFixtures.valid("good", "good-sprite").manifest.name
        guard case .zip(let data, let fileName) = m.exportOutcome(for: "good-sprite") else {
            Issue.record("expected a zip"); return
        }
        #expect(m.exportStatus == nil)
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("export-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent(fileName)
        m.writeExport(data, to: url, id: "good-sprite", fileName: fileName)
        #expect(m.exportStatus == "\(name) is exported to \(fileName).")
        #expect(try Data(contentsOf: url) == data)

        m.clearExportStatus()
        #expect(m.exportStatus == nil)
        m.writeExport(data, to: dir.appendingPathComponent("missing/x.zip"), id: "good-sprite", fileName: fileName)
        #expect(m.exportStatus == "\(name) couldn't be saved there, so nothing was exported.")
    }
}
