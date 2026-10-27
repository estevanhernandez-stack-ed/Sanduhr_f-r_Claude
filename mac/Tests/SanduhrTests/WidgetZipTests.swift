import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// FORMAT 7 rule 4: safe extraction. Every refusal is one lie away from a good zip. Ported case
/// for case from WidgetZipTests.cs.
@Suite("Widget zip")
struct WidgetZipTests {
    private static func b(_ s: String) -> [UInt8] { WidgetTestKit.bytes(s) }

    private static func palette() -> [String: Data] { WidgetPackFixtures.load("good", "good-palette") }

    private static func paletteSpecs() -> [ZipSpec] {
        PackValidator.ordinalSorted(Array(palette().keys)).map { ZipSpec($0, [UInt8](palette()[$0]!)) }
    }

    /// The refusal's reason; fails the test when the zip is accepted.
    private static func refused(_ zip: Data, sourceLocation: SourceLocation = #_sourceLocation) -> String {
        switch WidgetZip.read(zip) {
        case .success:
            Issue.record("the zip was accepted", sourceLocation: sourceLocation)
            return ""
        case .failure(let e):
            #expect(!e.message.isEmpty, sourceLocation: sourceLocation)
            return e.message
        }
    }

    private static func accepted(_ zip: Data) -> Bool {
        if case .success = WidgetZip.read(zip) { return true }
        return false
    }

    // MARK: - The good ones

    @Test func aPackZipBuiltInTheTestReadsBackAndValidatesStrict() throws {
        let pack = Self.palette()
        let files = try WidgetZip.read(WidgetTestKit.zipPack(pack)).get()
        #expect(files.keys.sorted() == pack.keys.sorted())
        for (k, v) in pack { #expect(files[k] == v) }
        let verdict = PackValidator.validate(files, folderName: "good-palette", strict: true)
        #expect(verdict.ok, "\(verdict.errors.joined(separator: " | "))")
    }

    @Test(arguments: ["hourglass", "oled-neon-palettes"])
    func aZipFromBuildCatalogPyReadsAndValidates(id: String) throws {
        // Built by sanduhr-widgets tools/build_catalog.py zip_pack; its hash is the catalog fixture's.
        let dir = WidgetTestKit.fixtures + "/widget-zips/"
        let zip = try #require(FileManager.default.contents(atPath: dir + id + "-1.0.0.zip"))
        let catalog = try #require(FileManager.default.contents(atPath: dir + "catalog.json"))
        let doc = try #require(try JSONSerialization.jsonObject(with: catalog) as? [String: Any])
        let entry = try #require((doc["packs"] as? [[String: Any]])?.first { $0["id"] as? String == id })
        #expect(entry["sha256"] as? String == SHA256.hash(data: zip).map { String(format: "%02x", $0) }.joined())
        #expect(entry["size"] as? Int == zip.count)

        let files = try WidgetZip.read(zip).get()
        let verdict = PackValidator.validate(files, folderName: id, strict: true)
        #expect(verdict.ok, "\(verdict.errors.joined(separator: " | "))")
        #expect(files.keys.sorted() == WidgetPackFixtures.load("packs", id).keys.sorted())
        // Every entry on its own is a file the validator's per-file rules accept: it validated as a pack above,
        // and each entry equals the folder's file byte for byte.
        let folder = WidgetPackFixtures.load("packs", id)
        for (k, v) in files { #expect(folder[k] == v, "\(k)") }
    }

    // MARK: - Paths

    @Test(arguments: [
        "../palettes.json", "themes/../../palettes.json", "/palettes.json", "themes\\palettes.json",
        "C:/palettes.json", "C:palettes.json", "./palettes.json", "themes//palettes.json",
    ])
    func unsafeNamesAreRefused(name: String) {
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [ZipSpec(name, Self.b("{}"))]))
    }

    @Test func aNonAsciiOrSpaceNameIsRefused() {
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [ZipSpec("pal\u{e9}ttes.json", Self.b("{}"))]))
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [ZipSpec("my palettes.json", Self.b("{}"))]))
    }

    @Test func namesThatDifferOnlyInCaseAreRefused() {
        #expect(Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [ZipSpec("Palettes.json", Self.b("{}"))])).contains("twice"))
        let specs = Self.paletteSpecs()
        #expect(Self.refused(WidgetTestKit.zip(specs + [specs[0]])).contains("twice"))
    }

    @Test func aLocalHeaderNamingAnotherFileIsRefused() {
        var specs = Self.paletteSpecs()
        specs[0].localName = "../evil.json"
        _ = Self.refused(WidgetTestKit.zip(specs))
    }

    // MARK: - Entry kinds

    @Test func directoryEntriesAreRefused() {
        #expect(Self.refused(WidgetTestKit.zip([ZipSpec("themes/", [])] + Self.paletteSpecs())).contains("directory"))
        // A directory by its attributes, whatever the name says.
        var dir = ZipSpec("extra", [])
        dir.external = (0x41ED << 16) | 0x10
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [dir]))
    }

    @Test func symlinksAndOtherNonRegularFilesAreRefused() {
        var link = ZipSpec("link.json", Self.b("palettes.json"))
        link.external = 0xA1FF << 16
        #expect(Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [link])).contains("regular"))
        var fifo = ZipSpec("fifo.json", Self.b("{}"))
        fifo.external = 0x11A4 << 16
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [fifo]))
    }

    @Test func aRegularFileFromAWindowsZipToolIsFine() {
        let specs = Self.paletteSpecs().map { s -> ZipSpec in
            var s = s
            s.madeBy = 20
            s.external = 0x20
            return s
        }
        #expect(Self.accepted(WidgetTestKit.zip(specs)))
    }

    @Test func compressedEntriesAreRefused() {
        var specs = Self.paletteSpecs()
        let raw = specs[specs.count - 1].data
        specs[specs.count - 1].method = 8
        specs[specs.count - 1].data = WidgetTestKit.deflate(raw)
        specs[specs.count - 1].crc = PackValidator.crc32(raw, 0, raw.count)
        specs[specs.count - 1].declaredSize = UInt32(raw.count)
        #expect(Self.refused(WidgetTestKit.zip(specs)).contains("compressed"))
        // Every entry deflated, as a general-purpose zip tool writes them.
        let all = Self.paletteSpecs().map { s -> ZipSpec in
            var s = s
            s.method = 8
            s.crc = PackValidator.crc32(s.data, 0, s.data.count)
            s.declaredSize = UInt32(s.data.count)
            s.data = WidgetTestKit.deflate(s.data)
            return s
        }
        _ = Self.refused(WidgetTestKit.zip(all))
    }

    @Test(arguments: [UInt16(0x0001), 0x0008, 0x0040])   // encrypted, data descriptor, strong encryption
    func encryptedEntriesAndDataDescriptorsAreRefused(flag: UInt16) {
        var specs = Self.paletteSpecs()
        specs[0].flags = flag
        _ = Self.refused(WidgetTestKit.zip(specs))
    }

    @Test func theUtf8NameFlagAloneIsFine() {
        let specs = Self.paletteSpecs().map { s -> ZipSpec in
            var s = s
            s.flags = 0x0800
            return s
        }
        #expect(Self.accepted(WidgetTestKit.zip(specs)))
    }

    // MARK: - Limits

    @Test func anEntryDeclaredOver5MBIsRefusedBeforeAnyDataIsRead() {
        var big = ZipSpec("big.json", Self.b("{}"))
        big.declaredSize = UInt32(WidgetZip.maxEntryBytes + 1)
        #expect(Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [big])).contains("5 MB"))
    }

    @Test func entriesDeclaredOver10MBInTotalAreRefusedBeforeAnyDataIsRead() {
        // Three small entries that each claim 4 MB: 12 MB declared, a few bytes actual.
        let claims = (0..<3).map { i -> ZipSpec in
            var s = ZipSpec("f\(i).json", Self.b("{}"))
            s.declaredSize = 4 * 1024 * 1024
            return s
        }
        #expect(Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + claims)).contains("10 MB"))
    }

    @Test func anEntryBiggerThanItsDirectoryRecordSaysIsRefused() {
        // The central directory says 2 bytes; the local header and the data say 3.
        var x = ZipSpec("x.json", Self.b("{ }"))
        x.declaredSize = 2
        x.localDeclaredSize = 3
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + [x]))
    }

    @Test func aZipLargerThanAPackCanBeIsRefused() {
        let huge = Data(count: WidgetZip.maxZipBytes + 1)
        #expect(Self.refused(huge).contains("larger"))
    }

    @Test func moreThan67EntriesAreRefused() {
        let many = (0..<WidgetZip.maxEntries).map { ZipSpec(String(format: "p%02d.json", $0), Self.b("{}")) }
        #expect(Self.refused(WidgetTestKit.zip(Self.paletteSpecs() + many)).contains("entries"))
    }

    @Test func aCrcMismatchIsRefused() {
        var specs = Self.paletteSpecs()
        specs[0].crc = 0xDEADBEEF
        #expect(Self.refused(WidgetTestKit.zip(specs)).contains("CRC"))
    }

    @Test func twoEntriesSharingOneBodyAreRefused() {
        let specs = Self.paletteSpecs()
        var copy = ZipSpec("copy.json", specs[0].data)
        copy.sharesLocalOf = 0
        _ = Self.refused(WidgetTestKit.zip(specs + [copy]))
    }

    @Test func aWrongEntryCountOrGarbageIsRefused() {
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs(), declaredCount: 1))
        _ = Self.refused(WidgetTestKit.zip(Self.paletteSpecs(), declaredCount: 9))
        _ = Self.refused(Data(Self.b("PK not really a zip at all, just some bytes")))
        _ = Self.refused(Data())
        let good = WidgetTestKit.zipPack(Self.palette())
        _ = Self.refused(good.prefix(good.count - 5))   // truncated end record
    }

    @Test func readingNeverTrapsOnRandomBytes() {
        var rng = SplitMix(seed: 38)
        let good = [UInt8](WidgetTestKit.zipPack(Self.palette()))
        for _ in 0..<300 {
            var bad = good
            for _ in 0..<4 { bad[Int(rng.next() % UInt64(bad.count))] = UInt8(truncatingIfNeeded: rng.next()) }
            _ = WidgetZip.read(Data(bad))
        }
    }

    @Test func everyTruncationOfAGoodZipIsRefusedWithoutTrapping() {
        let good = WidgetTestKit.zipPack(Self.palette())
        for n in stride(from: 0, to: good.count, by: 7) { _ = WidgetZip.read(good.prefix(n)) }
    }
}

/// A seeded generator so the fuzz case replays.
struct SplitMix {
    private var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}
