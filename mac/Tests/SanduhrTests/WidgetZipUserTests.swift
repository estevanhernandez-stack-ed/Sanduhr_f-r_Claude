import Foundation
import Testing
@testable import Sanduhr

/// A zip the user picked (`fromUser`, Install widget from file, #459): what Finder's Compress, Explorer and zip tools
/// write reads, the catalog path still refuses it, and inflating never runs past the declared size. Ported from the
/// "a zip the user picked" section of Windows' WidgetZipTests.cs (#469).
@Suite("Widget zip, a user's zip")
struct WidgetZipUserTests {
    private static func palette() -> [String: Data] { WidgetPackFixtures.load("good", "good-palette") }

    private static func paletteSpecs() -> [ZipSpec] {
        PackValidator.ordinalSorted(Array(palette().keys)).map { ZipSpec($0, [UInt8](palette()[$0]!)) }
    }

    private static func readsAsThePalette(_ zip: Data, sourceLocation: SourceLocation = #_sourceLocation) {
        switch WidgetZip.read(zip, fromUser: true) {
        case .failure(let e):
            Issue.record("refused: \(e.message)", sourceLocation: sourceLocation)
        case .success(let files):
            #expect(files.keys.sorted() == palette().keys.sorted(), sourceLocation: sourceLocation)
            for (k, v) in palette() { #expect(files[k] == v, "\(k)", sourceLocation: sourceLocation) }
            let verdict = PackValidator.validate(files, folderName: "good-palette", strict: true)
            #expect(verdict.ok, "\(verdict.errors.joined(separator: " | "))", sourceLocation: sourceLocation)
        }
    }

    private static func refusedFromUser(_ zip: Data, sourceLocation: SourceLocation = #_sourceLocation) -> String {
        switch WidgetZip.read(zip, fromUser: true) {
        case .success:
            Issue.record("the zip was accepted", sourceLocation: sourceLocation)
            return ""
        case .failure(let e):
            return e.message
        }
    }

    /// One deflated entry whose CRC and declared size describe `raw`, while its body is `body`.
    private static func deflated(_ name: String, raw: [UInt8], body: [UInt8]? = nil, declared: Int? = nil) -> ZipSpec {
        var s = ZipSpec(name, body ?? WidgetTestKit.deflate(raw))
        s.method = 8
        s.compressedIsData = true
        s.declaredSize = UInt32(declared ?? raw.count)
        s.crc = PackValidator.crc32(raw, 0, raw.count)
        return s
    }

    // MARK: - What a person's zip may be

    @Test func aDeflatedZipReadsBackByteForByte() {
        Self.readsAsThePalette(WidgetTestKit.toolZip(Self.palette()))
    }

    @Test func aZipWithDataDescriptorsReadsBack() {
        Self.readsAsThePalette(WidgetTestKit.toolZip(Self.palette(), streamed: true))
    }

    @Test func aFolderCompressedWholeReadsAsItsFilesWithoutTheFolderOrFindersExtras() {
        Self.readsAsThePalette(WidgetTestKit.toolZip(Self.palette(), folder: "My-palette"))
        Self.readsAsThePalette(WidgetTestKit.toolZip(Self.palette(), streamed: true, folder: "good-palette"))
    }

    @Test func aFolderWithASpaceInItsNameReadsAsItsFiles() {
        // Finder names the zip, and the folder inside it, as the person named the folder.
        Self.readsAsThePalette(WidgetTestKit.toolZip(Self.palette(), streamed: true, folder: "My Companion"))
    }

    @Test func aKeptFileWithASpaceIsStillRefusedByThePackRules() throws {
        var specs = Self.paletteSpecs()
        specs.append(ZipSpec("my file.json", [0x7B, 0x7D]))
        let files = try WidgetZip.read(WidgetTestKit.zip(specs), fromUser: true).get()
        let verdict = PackValidator.validate(files, folderName: "good-palette", strict: true)
        #expect(!verdict.ok)
        #expect(verdict.errors.contains { $0.contains("my file.json") }, "\(verdict.errors)")
        // The catalog path refuses the name outright, as before.
        guard case .failure(let e) = WidgetZip.read(WidgetTestKit.zip(specs)) else { Issue.record("accepted"); return }
        #expect(e.message.contains("not plain ASCII"))
    }

    @Test func aStoredZipStillReadsOnTheUserPath() {
        Self.readsAsThePalette(WidgetTestKit.zipPack(Self.palette()))
    }

    @Test func theCatalogPathStillRefusesWhatAUserMayPick() {
        for zip in [WidgetTestKit.toolZip(Self.palette()),
                    WidgetTestKit.toolZip(Self.palette(), streamed: true),
                    WidgetTestKit.toolZip(Self.palette(), folder: "My-palette")] {
            guard case .failure(let e) = WidgetZip.read(zip) else {
                Issue.record("the catalog path accepted a tool zip")
                continue
            }
            #expect(!e.message.isEmpty)
        }
        guard case .failure(let e) = WidgetZip.read(WidgetTestKit.toolZip(Self.palette())) else { return }
        #expect(e.message.contains("compressed entries are not allowed (pack zips are stored)"))
    }

    @Test func twoFoldersAtTheTopAreKeptAsFoldersAndJudgedByTheValidator() throws {
        var specs: [ZipSpec] = []
        for (i, key) in PackValidator.ordinalSorted(Array(Self.palette().keys)).enumerated() {
            specs.append(ZipSpec((i == 0 ? "b/" : "a/") + key, [UInt8](Self.palette()[key]!)))
        }
        let files = try WidgetZip.read(WidgetTestKit.zip(specs), fromUser: true).get()
        #expect(files.keys.contains { $0.hasPrefix("a/") } && files.keys.contains { $0.hasPrefix("b/") })
        #expect(!PackValidator.validate(files, folderName: "good-palette", strict: true).ok)
    }

    @Test func skippedEntriesDoNotCountTowardTheFileLimitButKeptFilesDo() {
        // Finder's twins and folder entries may run past 67 raw entries; kept files may not.
        var specs = Self.paletteSpecs()
        for i in 0..<80 { specs.append(ZipSpec("__MACOSX/._f\(i)", [1])) }
        Self.readsAsThePalette(WidgetTestKit.zip(specs))
        var many = Self.paletteSpecs()
        for i in 0..<(WidgetZip.maxEntries + 1 - many.count) { many.append(ZipSpec("x\(i).json", [0x7B, 0x7D])) }
        #expect(Self.refusedFromUser(WidgetTestKit.zip(many)).contains("a pack has at most"))
    }

    // MARK: - Inflating stays inside what was declared

    @Test func anEntryThatInflatesPastItsDeclaredSizeIsRefused() {
        let zeros = [UInt8](repeating: 0, count: 4 * 1024 * 1024)
        let bomb = Self.deflated("bomb.json", raw: Array(zeros.prefix(100)), body: WidgetTestKit.deflate(zeros))
        #expect(Self.refusedFromUser(WidgetTestKit.zip(Self.paletteSpecs() + [bomb])).contains("not the size it declares"))
    }

    @Test func anEntryDeclaredOver5MBIsRefusedBeforeInflating() {
        let big = Self.deflated("big.ttf", raw: [1, 2, 3], declared: PackValidator.maxFontBytes + 1)
        #expect(Self.refusedFromUser(WidgetTestKit.zip(Self.paletteSpecs() + [big])).contains("5 MB"))
    }

    @Test func anEntryThatInflatesShortOfItsDeclaredSizeIsRefused() {
        let raw = [UInt8](repeating: 0x41, count: 200)
        let short = Self.deflated("short.json", raw: raw, declared: 300)
        #expect(Self.refusedFromUser(WidgetTestKit.zip(Self.paletteSpecs() + [short])).contains("not the size it declares"))
    }

    @Test func aCrcMismatchAfterInflatingIsRefused() {
        var specs = Self.paletteSpecs()
        let raw = specs[0].data
        var s = Self.deflated(specs[0].name, raw: raw)
        s.crc = PackValidator.crc32(raw, 0, raw.count) ^ 1
        specs[0] = s
        #expect(Self.refusedFromUser(WidgetTestKit.zip(specs)).contains("CRC"))
    }

    @Test func damagedDeflateDataIsRefused() {
        var specs = Self.paletteSpecs()
        let raw = specs[0].data
        var body = WidgetTestKit.deflate(raw)
        for i in stride(from: 0, to: body.count, by: 3) { body[i] ^= 0xA5 }
        specs[0] = Self.deflated(specs[0].name, raw: raw, body: body)
        #expect(!Self.refusedFromUser(WidgetTestKit.zip(specs)).isEmpty)
    }

    @Test(arguments: [UInt16(0x0001), 0x0040])   // encrypted, strong encryption
    func aUsersZipStillMayNotBeEncrypted(flag: UInt16) {
        var specs = Self.paletteSpecs()
        specs[0].flags = flag
        #expect(Self.refusedFromUser(WidgetTestKit.zip(specs)).contains("encrypted"))
    }

    @Test func aMethodOtherThanDeflateIsRefusedInPlainWords() {
        var specs = Self.paletteSpecs()
        specs[0].method = 12   // bzip2
        #expect(Self.refusedFromUser(WidgetTestKit.zip(specs)).contains("only deflate"))
    }

    @Test func readingAUsersZipNeverTrapsOnRandomBytes() {
        var rng = SplitMix(seed: 459)
        let good = [UInt8](WidgetTestKit.toolZip(Self.palette(), streamed: true, folder: "p"))
        for _ in 0..<300 {
            var bad = good
            for _ in 0..<4 { bad[Int(rng.next() % UInt64(bad.count))] = UInt8(truncatingIfNeeded: rng.next()) }
            _ = WidgetZip.read(Data(bad), fromUser: true)
        }
        for n in stride(from: 0, to: good.count, by: 11) { _ = WidgetZip.read(Data(good.prefix(n)), fromUser: true) }
    }
}
