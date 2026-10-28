import AppKit
import CoreText
import Foundation
import Testing
@testable import Sanduhr

/// Font packs (spec 11): faces registered for this process only, unregistered with the pack, a face
/// CoreText refuses reported on the row, and the registered families in the pickers. The fixtures are
/// `Fixtures/font-packs/real-font` (a tiny generated font, OFL-1.1) and `refused-font` (a font file cut
/// short that still carries a valid magic number, so it passes the pack checks and CoreText refuses it).
/// Serialized: process-scope registration is shared by every test in the process.
@Suite("Font packs", .serialized)
struct PackFontsTests {
    static let family = "Fixture Pack Sans"

    static func files(_ name: String) -> [String: Data] {
        let dir = Bundle.module.resourceURL!.appendingPathComponent("Fixtures/font-packs/" + name)
        var out: [String: Data] = [:]
        for rel in (try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? [] where !rel.hasPrefix(".") {
            out[rel] = FileManager.default.contents(atPath: dir.appendingPathComponent(rel).path)
        }
        return out
    }

    static func install(_ name: String, in store: WidgetStore, version: String? = nil) throws {
        var files = Self.files(name)
        if let version { files = StoreFixtures.withVersion(files, version) }
        let v = PackValidator.validate(files, folderName: name, strict: true)
        #expect(v.ok, "\(v.errors.joined(separator: " | "))")
        let result = store.install(files, try #require(v.manifest), source: .catalog)
        #expect(result.ok, "\(result.error ?? "")")
    }

    /// Whether CoreText lists the family for this process.
    static func processKnows(_ family: String) -> Bool {
        let names = CTFontManagerCopyAvailableFontFamilyNames() as? [String] ?? []
        return names.contains(family)
    }

    @Test func bothFixturesPassThePackChecks() {
        for name in ["real-font", "refused-font"] {
            let v = PackValidator.validate(Self.files(name), folderName: name, strict: true)
            #expect(v.ok, "\(name): \(v.errors.joined(separator: " | "))")
        }
    }

    @Test func registersAnInstalledPackForThisProcessAndUnregistersItOnRelease() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let fonts = PackFonts()
        try Self.install("real-font", in: store)
        #expect(!Self.processKnows(Self.family))

        #expect(fonts.sync(store))
        #expect(fonts.families == [Self.family])
        #expect(fonts.families(of: "real-font") == [Self.family])
        #expect(fonts.refusedCount(of: "real-font") == 0)
        #expect(fonts.note(for: "real-font") == nil)
        #expect(Self.processKnows(Self.family))
        #expect(NSFontManager.shared.availableMembers(ofFontFamily: Self.family) != nil)
        #expect(!fonts.sync(store), "a second sync with nothing changed does nothing")

        fonts.release("real-font")
        #expect(fonts.families.isEmpty)
        #expect(!Self.processKnows(Self.family))
    }

    @Test func aFaceCoreTextRefusesIsReportedAndLeftOutWhileThePackStaysInstalled() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let fonts = PackFonts()
        try Self.install("refused-font", in: store)
        fonts.sync(store)
        #expect(fonts.families.isEmpty)
        #expect(fonts.refusedCount(of: "refused-font") == 1)
        #expect(fonts.note(for: "refused-font") == "One font couldn't be loaded and is left out.")
        #expect(store.load().find("refused-font") != nil)
        #expect(store.installed(ofKind: "font").map(\.row.id) == ["refused-font"])
    }

    @Test func aRefusedFaceDoesNotTakeTheGoodOneWithIt() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        // A seam: CoreText "refuses" the file whose name carries "bad", the real font registers.
        var asked: [String] = []
        let fonts = PackFonts(register: { url in
            asked.append(url.lastPathComponent)
            return url.lastPathComponent.contains("bad") ? .refused : PackFonts.coreTextRegister(url)
        })
        try Self.install("real-font", in: store)
        let packDir = store.packDir("real-font")
        try FileManager.default.copyItem(at: packDir.appendingPathComponent("fixture-sans.ttf"),
                                         to: packDir.appendingPathComponent("bad-copy.ttf"))
        fonts.sync(store)
        #expect(asked == ["bad-copy.ttf", "fixture-sans.ttf"])
        #expect(fonts.families == [Self.family])
        #expect(fonts.refusedCount(of: "real-font") == 1)
        fonts.release("real-font")
    }

    @Test func refusedSentenceCountsPlainly() {
        #expect(PackFonts.refusedNote(0) == nil)
        #expect(PackFonts.refusedNote(1) == "One font couldn't be loaded and is left out.")
        #expect(PackFonts.refusedNote(3) == "3 fonts couldn't be loaded and are left out.")
    }

    @Test func aDisabledPacksFontsLeaveTheListsAndComeBackWhenEnabled() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let fonts = PackFonts()
        try Self.install("real-font", in: store)
        fonts.sync(store)
        #expect(DeskFont.pickerFamilies(installed: ["Menlo"], packs: fonts.families).contains(Self.family))

        let on = try String(contentsOf: store.registryFile, encoding: .utf8)
        try Data(on.replacingOccurrences(of: "\"enabled\": true", with: "\"enabled\": false").utf8).write(to: store.registryFile)
        #expect(fonts.sync(store))
        #expect(fonts.families.isEmpty)
        #expect(!DeskFont.pickerFamilies(installed: ["Menlo"], packs: fonts.families).contains(Self.family))
        #expect(!Self.processKnows(Self.family))

        try Data(on.utf8).write(to: store.registryFile)
        fonts.sync(store)
        #expect(Self.processKnows(Self.family))
        fonts.release("real-font")
    }

    @Test func anUpdatedPackIsRegisteredAgain() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        var registered = 0
        var unregistered = 0
        let fonts = PackFonts(register: { registered += 1; return PackFonts.coreTextRegister($0) },
                              unregister: { unregistered += 1; PackFonts.coreTextUnregister($0) })
        try Self.install("real-font", in: store)
        fonts.sync(store)
        try Self.install("real-font", in: store, version: "1.0.1")
        #expect(fonts.sync(store))
        #expect(registered == 2)
        #expect(unregistered == 1)
        #expect(fonts.families == [Self.family])
        fonts.release("real-font")
    }

    @Test func pickerListsPutPackFamiliesAfterTheBuiltInsOnceEach() {
        let list = DeskFont.pickerFamilies(installed: ["Zapfino", "Menlo", "fixture pack sans"],
                                           packs: [Self.family, "Menlo", "Aardvark Sans"])
        #expect(list == ["EsteFont Pro", "EsteFont 26", "Aardvark Sans", "fixture pack sans", "Menlo", "Zapfino"])
        #expect(DeskFont.pickerFamilies(installed: [], packs: [Self.family, "EsteFont Pro"])
            == ["EsteFont Pro", "EsteFont 26", Self.family])
        #expect(DeskFont.pickerFamilies(installed: [], packs: []) == ["EsteFont Pro", "EsteFont 26"])
    }

    @MainActor
    @Test func installedFontsReachThePickersAndUninstallTakesThemOut() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let fonts = PackFonts()
        try Self.install("real-font", in: store)
        let model = WidgetsModel(store: store, defaults: UserDefaults(suiteName: "sanduhr-packfonts-" + UUID().uuidString)!,
                                 fonts: fonts, changed: { fonts.sync(store) })
        fonts.sync(store)
        #expect(DeskFont.pickerFamilies(installed: [], packs: fonts.families).contains(Self.family))
        // The same list the pickers build: the process-registered family is in what AppKit lists too, once.
        let all = DeskFont.pickerFamilies(installed: NSFontManager.shared.availableFontFamilies, packs: fonts.families)
        #expect(all.filter { $0 == Self.family }.count == 1)
        let row = try #require(model.installed.first)
        #expect(model.fontNote(row) == nil)

        model.uninstall("real-font")
        #expect(model.notice == nil)
        #expect(fonts.families.isEmpty)
        #expect(!Self.processKnows(Self.family))
        #expect(!DeskFont.pickerFamilies(installed: [], packs: fonts.families).contains(Self.family))
    }

    @MainActor
    @Test func aRefusedFacesSentenceShowsOnThePacksRow() throws {
        let root = WidgetTestRoot()
        let store = root.store()
        let fonts = PackFonts()
        try Self.install("refused-font", in: store)
        let model = WidgetsModel(store: store, defaults: UserDefaults(suiteName: "sanduhr-packfonts-" + UUID().uuidString)!,
                                 fonts: fonts, changed: { fonts.sync(store) })
        fonts.sync(store)
        let row = try #require(model.installed.first)
        #expect(model.fontNote(row) == "One font couldn't be loaded and is left out.")
    }
}
