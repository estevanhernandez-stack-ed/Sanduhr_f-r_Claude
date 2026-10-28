import Foundation
import Testing
@testable import Sanduhr

/// "Use my own art..." (spec 3.8 section 6): the pack image rules on a picked PNG and the pack rebuilt around
/// it and validated whole. Ported case for case from WidgetOwnArtTests.cs, less the store-install case (it
/// needs the store's own-art install rule, which is not part of this port).
@Suite("Widget own art")
struct WidgetOwnArtTests {
    private static let stage8 = CompanionStage(id: "only", name: "only", minGrowth: 0, sheet: "sheet.json", frameWidth: 8, frameHeight: 8)

    /// An opaque RGBA sheet of `w` x `h`.
    static func sheet(_ w: Int, _ h: Int) -> Data {
        var rgba = [UInt8](repeating: 0, count: w * h * 4)
        var i = 0
        while i < rgba.count { rgba[i] = 0x22; rgba[i + 1] = 0xd3; rgba[i + 2] = 0xee; rgba[i + 3] = 255; i += 4 }
        return PackValidatorTests.png(w, h, rgba)
    }

    private static func claudeSprite() -> WidgetProposalPack {
        WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: WidgetProposalTests.now).pack!
    }

    @Test func thePackImageRulesAreCheckedBeforeAnythingIsCopied() {
        let s = Self.stage8
        #expect(WidgetOwnArt.checkImage(Self.sheet(32, 8), stage: s, maxFrame: 3) == nil)
        #expect(WidgetOwnArt.checkImage(Data("GIF89a not a png at all, padded to length".utf8), stage: s, maxFrame: 3)?.contains("isn't a PNG") == true)
        #expect(WidgetOwnArt.checkImage(Data(count: PackValidator.maxImageBytes + 1), stage: s, maxFrame: 3)?.contains("larger than 2 MB") == true)
        #expect(WidgetOwnArt.checkImage(Self.sheet(30, 8), stage: s, maxFrame: 3)?.contains("isn't a whole number of 8x8 cells") == true)
        #expect(WidgetOwnArt.checkImage(Self.sheet(16, 8), stage: s, maxFrame: 3)?.contains("needs at least 4") == true)
        let wide = Data(Self.sheet(8, 8).prefix(16)) + Data([0, 0, 0x20, 0, 0, 0, 0, 8]) + Data(count: 20)
        #expect(WidgetOwnArt.checkImage(wide, stage: s, maxFrame: 0)?.contains("at most 4096 pixels") == true)
    }

    @Test func theImageSentencesAreWindowsWordsForWord() {
        let s = Self.stage8
        #expect(WidgetOwnArt.checkImage(Self.sheet(30, 8), stage: s, maxFrame: 3)
            == "That image is 30x8, which isn't a whole number of 8x8 cells (the size of only's frames).")
        #expect(WidgetOwnArt.checkImage(Self.sheet(8, 8), stage: s, maxFrame: 3)
            == "That sheet has 1 cell, but the animations use frame 3, so it needs at least 4.")
        #expect(WidgetOwnArt.checkImage(Self.sheet(16, 8), stage: s, maxFrame: 3)
            == "That sheet has 2 cells, but the animations use frame 3, so it needs at least 4.")
        #expect(WidgetOwnArt.checkImage(Data(count: 3), stage: s, maxFrame: 0) == "That file isn't a PNG image. A sprite sheet is a PNG.")
        #expect(WidgetOwnArt.artPath("egg") == "art/egg.png")
    }

    @Test func theArtReplacesThePixelSheetAndThePackIsValidatedWhole() throws {
        let pack = Self.claudeSprite()
        let result = WidgetOwnArt.apply(pack.files, packId: pack.id, art: ["only": Self.sheet(32, 8)])
        #expect(result.ok, "\(result.errors)")
        let files = try #require(result.files)
        #expect(WidgetProposal.ordinalSorted(Array(files.keys)) == ["LICENSE", "art/sprite.png", "pack.json", "sprite.json"])
        #expect(TJ.get(TJ.text(files["sprite.json"]!), ["sheet"])?.string == "art/sprite.png")
        #expect(result.manifest?.version == "1.0.1")
        #expect(result.manifest?.files == ["art/sprite.png", "sprite.json"])
        #expect(PackValidator.validate(files, folderName: pack.id, strict: true).ok)
    }

    @Test func anImageWithABrokenChunkIsRefusedByTheWholePackCheck() {
        let pack = Self.claudeSprite()
        var png = [UInt8](Self.sheet(32, 8))
        png[png.count - 5] ^= 0xff   // the IEND CRC
        let result = WidgetOwnArt.apply(pack.files, packId: pack.id, art: ["only": Data(png)])
        #expect(!result.ok)
        #expect(result.errors.contains { $0.hasPrefix("art/sprite.png") }, "\(result.errors)")
    }

    @Test func noImageOrAnUnknownStageChangesNothing() {
        let pack = Self.claudeSprite()
        #expect(!WidgetOwnArt.apply(pack.files, packId: pack.id, art: [:]).ok)
        #expect(WidgetOwnArt.apply(pack.files, packId: pack.id, art: [:]).errors == ["No image was chosen, so the widget is unchanged."])
        #expect(WidgetOwnArt.apply(pack.files, packId: pack.id, art: ["egg": Self.sheet(32, 8)]).errors[0].contains("no stage"))
        var notSprite = pack.files
        notSprite["sprite.json"] = Data("{}".utf8)
        #expect(WidgetOwnArt.apply(notSprite, packId: pack.id, art: ["only": Self.sheet(32, 8)]).errors == ["This widget isn't a sprite Sanduhr can read."])
    }

    @Test func aStagedSpriteTakesTheArtPerStageAndKeepsTheOthersSheets() throws {
        let files = WidgetPackFixtures.load("good", "good-sprite-stages")
        let sprite = try #require(WidgetOwnArt.sprite(files))
        let first = try #require(sprite.stages.first)
        let result = WidgetOwnArt.apply(files, packId: "good-sprite-stages",
                                        art: [first.id: Self.sheet(first.frameWidth * (sprite.maxFrame + 1), first.frameHeight)])
        #expect(result.ok, "\(result.errors)")
        #expect(result.files?[WidgetOwnArt.artPath(first.id)] != nil)
        let stages = TJ.get(TJ.text(result.files!["sprite.json"]!), ["stages"])?.array
        #expect(stages?.first?.object?["sheet"]?.string == WidgetOwnArt.artPath(first.id))
    }

    @Test func bumpPatchStepsThePatchOnly() {
        #expect(WidgetOwnArt.bumpPatch("1.0.0") == "1.0.1")
        #expect(WidgetOwnArt.bumpPatch("2.3.9") == "2.3.10")
        #expect(WidgetOwnArt.bumpPatch("odd") == "odd")
        #expect(WidgetOwnArt.bumpPatch("1.0.2147483647") == "1.0.2147483647")
    }
}
