import Foundation
import Testing
@testable import Sanduhr

@Suite("Widget file install")
struct WidgetFileInstallTests {
    private static func zip(_ files: [String: Data]) -> Data { WidgetTestKit.zipPack(files) }

    private static func text(_ d: Data) -> String { String(decoding: d, as: UTF8.self) }

    private static func refusal(_ o: WidgetFileInstall.Outcome) -> String {
        if case .refused(let s) = o { return s }
        Issue.record("expected a refusal, got \(o)")
        return ""
    }

    private static func diskFiles(_ store: WidgetStore, _ id: String) -> [String: Data] {
        let dir = store.packDir(id)
        return StoreFixtures.relativeFiles(in: dir).filter { !$0.hasPrefix(WidgetStore.rasterName) }
            .reduce(into: [:]) { $0[$1] = try? Data(contentsOf: dir.appendingPathComponent($1)) }
    }

    @Test func aGoodPackInstallsAsMadeHere() {
        let root = WidgetTestRoot(), store = root.store()
        let files = WidgetPackFixtures.load("good", "good-palette")
        let outcome = WidgetFileInstall.install(zip: Self.zip(files), store: store)
        guard case .installed(let m, let wasUpdate) = outcome else { Issue.record("\(outcome)"); return }
        #expect(m.id == "good-palette" && !wasUpdate)
        let row = store.load().find("good-palette")
        #expect(row?.source == .local && row?.madeHere == true)
        #expect(Self.diskFiles(store, "good-palette") == files)
    }

    @Test(arguments: [
        ("bad", "bad-no-license", "LICENSE is missing"),
        ("bad", "bad-trademark-name", "trademark"),
        ("bad", "bad-preview-too-wide", "limit is 4096 pixels"),
    ])
    func aBadPackInstallsNothingAndCarriesTheValidatorsSentence(group: String, name: String, needle: String) {
        let root = WidgetTestRoot(), store = root.store()
        let files = WidgetPackFixtures.load(group, name)
        let why = Self.refusal(WidgetFileInstall.install(zip: Self.zip(files), store: store))
        #expect(why.contains(needle), "\(why)")
        #expect(store.load().packs.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: store.packDir(name).path))
    }

    @Test func moreThanThreeErrorsAreCounted() {
        var files = WidgetPackFixtures.load("good", "good-palette")
        files["LICENSE"] = nil
        files["extra.txt"] = Data("x".utf8)
        files["extra2.txt"] = Data("x".utf8)
        files["extra3.txt"] = Data("x".utf8)
        files["pack.json"] = Data(Self.text(files["pack.json"]!)
            .replacingOccurrences(of: "Fixture palette", with: "Claude palette").utf8)
        let root = WidgetTestRoot(), store = root.store()
        let verdict = PackValidator.validate(files, folderName: "good-palette", strict: true)
        #expect(verdict.errors.count > 3, "\(verdict.errors)")
        let why = Self.refusal(WidgetFileInstall.install(zip: Self.zip(files), store: store))
        #expect(why == WidgetFileInstall.rejected(verdict.errors))
        #expect(why.hasSuffix(" and \(verdict.errors.count - 3) more."), "\(why)")
        #expect(why.hasPrefix(verdict.errors[0]))
        #expect(store.load().packs.isEmpty)
    }

    @Test func theJoinShowsThreeAndCountsTheRest() {
        let five = ["A.", "B.", "C.", "D.", "E."]
        #expect(WidgetFileInstall.rejected(five) == "A. B. C. and 2 more.")
        #expect(WidgetFileInstall.rejected(Array(five.prefix(3))) == "A. B. C.")
        #expect(WidgetFileInstall.rejected(Array(five.prefix(2))) == "A. B.")
        // The validator's own messages have no closing period; the status line adds one.
        #expect(WidgetFileInstall.rejected(["No period", "Has one."]) == "No period. Has one.")
    }

    @Test func theSameIdTwiceEndsAsOnePackWithTheSecondZipsFiles() {
        let root = WidgetTestRoot(), store = root.store()
        let first = WidgetPackFixtures.load("good", "good-palette")
        let second = StoreFixtures.withVersion(first, "1.0.1")
        guard case .installed(_, let u1) = WidgetFileInstall.install(zip: Self.zip(first), store: store),
              case .installed(let m2, let u2) = WidgetFileInstall.install(zip: Self.zip(second), store: store)
        else { Issue.record("an install was refused"); return }
        #expect(!u1 && u2 && m2.version == "1.0.1")
        #expect(store.load().packs.filter { $0.id == "good-palette" }.count == 1)
        #expect(Self.diskFiles(store, "good-palette") == second)
    }

    @Test func aCatalogPackOfTheSameIdIsRefusedAndUnchanged() {
        let root = WidgetTestRoot(), store = root.store()
        let (files, manifest) = StoreFixtures.valid("good", "good-palette")
        #expect(store.install(files, manifest, source: .catalog).ok)
        let other = StoreFixtures.withVersion(files, "9.9.9")
        let why = Self.refusal(WidgetFileInstall.install(zip: Self.zip(other), store: store))
        #expect(why == "A catalog widget is already installed as good-palette. Uninstall it first.")
        #expect(store.load().find("good-palette")?.source == .catalog)
        #expect(store.load().find("good-palette")?.version == "1.0.0")
        #expect(Self.diskFiles(store, "good-palette") == files)
    }

    @Test func aMinimumAboveSupportedIsRefusedBeforeValidation() {
        let root = WidgetTestRoot(), store = root.store()
        var files = WidgetPackFixtures.load("good", "good-palette")
        files["LICENSE"] = nil   // would fail validation too; the floor must speak first
        let why = Self.refusal(WidgetFileInstall.install(zip: Self.zip(files), store: store, supported: "3.7.0"))
        #expect(why == "Fixture palette needs a newer Sanduhr (it asks for widget format 3.8.0).")
        #expect(store.load().packs.isEmpty)
    }

    @Test func aZipTheReaderRefusesInstallsNothing() {
        let root = WidgetTestRoot(), store = root.store()
        let files = WidgetPackFixtures.load("good", "good-palette")
        var link = PackValidator.ordinalSorted(Array(files.keys)).map { ZipSpec($0, [UInt8](files[$0]!)) }
        link[0].external = 0xA1FF << 16   // symlink
        var big = PackValidator.ordinalSorted(Array(files.keys)).map { ZipSpec($0, [UInt8](files[$0]!)) }
        big[0].declaredSize = UInt32(PackValidator.maxFontBytes + 1)
        for specs in [link, big] {
            let why = Self.refusal(WidgetFileInstall.install(zip: WidgetTestKit.zip(specs), store: store))
            #expect(!why.isEmpty)
            // Not a format problem, so no "how to make one" hint.
            #expect(!why.contains(WidgetFileInstall.zipFormatHint))
        }
        #expect(store.load().packs.isEmpty)
    }

    /// Finder's Compress writes deflate entries with data descriptors and folder entries; each of those refusals
    /// ends with how to make a pack zip.
    @Test func aFinderStyleZipIsRefusedWithHowToMakeOne() {
        let root = WidgetTestRoot(), store = root.store()
        let files = WidgetPackFixtures.load("good", "good-palette")
        let plain = PackValidator.ordinalSorted(Array(files.keys)).map { ZipSpec($0, [UInt8](files[$0]!)) }
        var descriptor = plain; descriptor[0].flags = 0x0008
        var deflated = plain; deflated[0].method = 8
        let folder = [ZipSpec("art/", [])] + plain
        for specs in [descriptor, deflated, folder] {
            let why = Self.refusal(WidgetFileInstall.install(zip: WidgetTestKit.zip(specs), store: store))
            #expect(why.hasPrefix("That file isn't a widget pack Sanduhr can open ("), "\(why)")
            #expect(why.hasSuffix(" " + WidgetFileInstall.zipFormatHint), "\(why)")
        }
        #expect(WidgetFileInstall.zipFormatHint.contains("zip -0 -r -D"))
        #expect(!WidgetFileInstall.zipFormatHint.contains("\u{2014}"))
        #expect(store.load().packs.isEmpty)
    }

    @Test func aZipOverTheCapIsRefusedUnread() {
        let root = WidgetTestRoot(), store = root.store()
        let huge = Data(count: WidgetZip.maxZipBytes + 1)
        #expect(Self.refusal(WidgetFileInstall.install(zip: huge, store: store)) == WidgetFileInstall.tooLarge)
        #expect(store.load().packs.isEmpty)
    }

    @Test func isSpriteIsASpriteJsonAtTheRoot() {
        #expect(WidgetFileInstall.isSprite(WidgetPackFixtures.load("good", "good-sprite")))
        #expect(!WidgetFileInstall.isSprite(WidgetPackFixtures.load("good", "good-palette")))
    }

    @Test func theLines() {
        let m = StoreFixtures.valid("good", "good-palette").manifest
        func line(_ up: Bool, _ sprite: Bool, _ desk: Bool, _ shown: Bool) -> String {
            WidgetFileInstall.line(for: m, wasUpdate: up, isSprite: sprite, deskOn: desk, companionShown: shown)
        }
        let n = m.name
        let all = [
            line(false, true, true, true), line(false, true, false, true), line(false, true, true, false),
            line(false, false, true, true), line(true, false, true, true),
        ]
        #expect(all[0] == "\(n) is on your Desk now. It came from your file and stays on this Mac.")
        #expect(all[1] == "\(n) is your companion now. Turn the Desk on in Settings, Desk to see it.")
        #expect(all[2] == "\(n) is your companion now. It is hidden; tick Companion in Settings, Desk to show it.")
        #expect(all[3] == "\(n) is installed from your file. It stays on this Mac.")
        #expect(all[4] == "\(n) is updated from your file. It stays on this Mac.")
        for s in all + [WidgetFileInstall.tooLarge] { #expect(!s.contains("\u{2014}")) }
    }
}
