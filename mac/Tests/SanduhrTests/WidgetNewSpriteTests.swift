import Foundation
import Testing
@testable import Sanduhr

/// "New sprite from an image..." (spec 2.39.0 section 18): the rules on a picked sheet, its cell size and tags,
/// and the pack made from them, validated whole.
@Suite("Widget new sprite")
struct WidgetNewSpriteTests {
    private static let now = Date(timeIntervalSince1970: 1_790_000_000)

    /// Four 8x8 cells in a row, or whatever `w` x `h` is.
    private static func input(_ w: Int = 32, _ h: Int = 16, cell: Int = 8, name: String = "My Blob",
                              scale: Int? = nil, tags: [String: [Int]]? = nil) -> WidgetNewSprite.Input {
        WidgetNewSprite.Input(png: WidgetOwnArtTests.sheet(w, h), cellWidth: cell, cellHeight: cell, name: name,
                              scale: scale, tags: tags ?? ["walk": [0, 1, 2, 3], "sit": [4, 5]])
    }

    private static func make(_ i: WidgetNewSprite.Input, installed: [String] = []) -> OwnArtResult {
        WidgetNewSprite.make(i, installedIds: installed, now: now)
    }

    private func reason(_ r: OwnArtResult) -> String { r.errors.joined(separator: " ") }

    @Test func aMadePackPassesTheValidatorStrict() throws {
        let r = Self.make(Self.input(tags: ["walk": [0, 1, 2, 3], "sit": [4, 5], "nap": [6], "cheer": [7, 6], "worry": [5]]))
        #expect(r.ok, "\(r.errors)")
        let files = try #require(r.files)
        #expect(WidgetProposal.ordinalSorted(Array(files.keys)) == ["LICENSE", "art/sprite.png", "pack.json", "sprite.json"])
        #expect(PackValidator.validate(files, folderName: "my-my-blob", strict: true).ok)
        let m = try #require(r.manifest)
        #expect(m.id == "my-my-blob")
        #expect(m.license == "CC0-1.0")
        #expect(m.version == "1.0.0")
        #expect(m.sanduhrMin == "3.8.0")
        #expect(m.kind == "sprite")
        #expect(m.files == ["art/sprite.png", "sprite.json"])
    }

    @Test func theUsersBytesAreCopiedUnchanged() throws {
        let i = Self.input()
        let files = try #require(Self.make(i).files)
        #expect(files["art/sprite.png"] == i.png)
    }

    @Test func theTagsFillTheNineAnimationsTheWayTheHourglassMapsThem() throws {
        let files = try #require(Self.make(Self.input(tags: ["walk": [0, 1, 2], "sit": [4, 5], "nap": [6], "cheer": [7], "worry": [3]])).files)
        let sprite = try #require(CompanionSprite.parse(files["sprite.json"]!))
        #expect(sprite.stages.count == 1)
        #expect(sprite.stages[0].sheet == "art/sprite.png")
        #expect(sprite.frames("walk") == [0, 1, 2])
        #expect(sprite.frames("sit") == [4, 5])
        #expect(sprite.frames("pop") == [7])
        #expect(sprite.frames("celebrate") == [7])
        #expect(sprite.frames("focus") == [4, 5])
        #expect(sprite.frames("sleep") == [6])
        #expect(sprite.moods[.content] == "sit")
        #expect(sprite.moods[.focused] == "focus")
        #expect(sprite.moods[.worried] == "worry")
        #expect(sprite.moods[.exhausted] == "sleep")
        #expect(sprite.moods[.celebrate] == "celebrate")
        #expect(sprite.triggers[.needsYou] == "pop")
        #expect(sprite.triggers[.jobPassed] == "cheer")
        #expect(sprite.triggers[.nearLimit] == "worry")
        #expect(sprite.triggers[.idle] == "nap")
        #expect(sprite.scale == 2)
        #expect(sprite.fps == 8)
    }

    @Test func anUntaggedActionFallsBackToSit() throws {
        let files = try #require(Self.make(Self.input()).files)
        let sprite = try #require(CompanionSprite.parse(files["sprite.json"]!))
        for a in ["nap", "cheer", "worry", "pop", "focus", "sleep", "celebrate"] { #expect(sprite.frames(a) == [4, 5], "\(a)") }
        #expect(sprite.frames("walk") == [0, 1, 2, 3])
    }

    @Test func aScaleIsKeptInTheValidatorsRange() throws {
        let files = try #require(Self.make(Self.input(scale: 3)).files)
        #expect(CompanionSprite.parse(files["sprite.json"]!)?.scale == 3)
        #expect(reason(Self.make(Self.input(scale: 0))).contains("Scale is 1 to 4"))
        #expect(reason(Self.make(Self.input(scale: 5))).contains("Scale is 1 to 4"))
    }

    @Test func aCellMustBe8To64PixelsOnASide() {
        #expect(reason(Self.make(Self.input(64, 16, cell: 4))).contains("8 to 64 pixels"))
        #expect(reason(Self.make(Self.input(128, 128, cell: 65))).contains("8 to 64 pixels"))
        #expect(Self.make(Self.input(64, 64, cell: 64, tags: ["walk": [0, 0], "sit": [0]])).ok)
        var wide = Self.input(32, 16)
        wide.cellHeight = 7
        #expect(reason(Self.make(wide)).contains("8 to 64 pixels"))
    }

    @Test func theSheetMustBeAWholeNumberOfCells() {
        #expect(reason(Self.make(Self.input(30, 16))) == "That image is 30x16, which isn't a whole number of 8x8 cells.")
        #expect(reason(Self.make(Self.input(32, 12))).contains("whole number"))
    }

    @Test func cellsAreNumberedRowMajorAndMustExist() {
        // 32x16 at 8x8 is 8 cells (two rows of four): 7 is the last one.
        #expect(Self.make(Self.input(tags: ["walk": [3, 4], "sit": [7]])).ok)
        #expect(reason(Self.make(Self.input(tags: ["walk": [0, 1], "sit": [8]]))).contains("the sheet has 8 cells"))
        #expect(reason(Self.make(Self.input(tags: ["walk": [0, -1], "sit": [1]]))).contains("walk cells"))
    }

    @Test func walkNeedsTwoOrMoreCells() {
        #expect(reason(Self.make(Self.input(tags: ["walk": [0], "sit": [1]]))) == "Walk needs two or more cells, in order. It has 1.")
        #expect(reason(Self.make(Self.input(tags: ["sit": [1]]))).contains("Walk needs two or more"))
    }

    @Test func sitIsRequiredBecauseEverythingElseFallsBackToIt() {
        #expect(reason(Self.make(Self.input(tags: ["walk": [0, 1]]))).contains("Tag at least one cell as sit"))
        #expect(reason(Self.make(Self.input(tags: ["walk": [0, 1], "sit": []]))).contains("Tag at least one cell as sit"))
    }

    @Test func anUnknownActionIsRefused() {
        #expect(reason(Self.make(Self.input(tags: ["walk": [0, 1], "sit": [2], "dance": [3]]))).contains("dance isn't an action"))
    }

    @Test func aNonPngAndAnOversizeImageAreRefusedWithTheOwnArtSentences() {
        var i = Self.input()
        i.png = Data("GIF89a not a png at all, padded to length".utf8)
        #expect(reason(Self.make(i)) == "That file isn't a PNG image. A sprite sheet is a PNG.")
        i.png = Data(count: PackValidator.maxImageBytes + 1)
        #expect(reason(Self.make(i)).contains("larger than 2 MB"))
        let wide = Data(WidgetOwnArtTests.sheet(8, 8).prefix(16)) + Data([0, 0, 0x20, 0, 0, 0, 0, 8]) + Data(count: 20)
        i.png = wide
        #expect(reason(Self.make(i)).contains("at most 4096 pixels"))
    }

    @Test func aBrokenChunkIsCaughtByTheWholePackCheck() {
        var i = Self.input()
        var bytes = [UInt8](i.png)
        bytes[bytes.count - 20] ^= 0xFF
        i.png = Data(bytes)
        let r = Self.make(i)
        #expect(!r.ok)
        #expect(!r.errors.isEmpty)
    }

    @Test func theNameBecomesAnIdAndCollisionsTakeANumber() {
        #expect(WidgetNewSprite.packId(name: "  Neon Blob!! ", installedIds: []) == "my-neon-blob")
        #expect(WidgetNewSprite.packId(name: "Neon Blob", installedIds: ["my-neon-blob"]) == "my-neon-blob-2")
        #expect(WidgetNewSprite.packId(name: "Neon Blob", installedIds: ["my-neon-blob", "my-neon-blob-2"]) == "my-neon-blob-3")
        #expect(WidgetNewSprite.packId(name: "Pixel Pal", installedIds: ["my-neon-blob"]) == "my-pixel-pal")
        #expect(WidgetNewSprite.packId(name: "!!!", installedIds: []) == nil)
        let long = String(repeating: "a", count: 60)
        let id = WidgetNewSprite.packId(name: long, installedIds: [])!
        #expect(id.count == 40)
        #expect(WidgetNewSprite.packId(name: long, installedIds: [id])!.count <= 40)
        #expect(WidgetNewSprite.packId(name: long, installedIds: [id]) != id)
    }

    @Test func aCollidingNameMakesAPackWithTheNextId() throws {
        let r = Self.make(Self.input(), installed: ["my-my-blob"])
        #expect(r.ok, "\(r.errors)")
        #expect(r.manifest?.id == "my-my-blob-2")
        #expect(PackValidator.validate(try #require(r.files), folderName: "my-my-blob-2", strict: true).ok)
    }

    @Test func aNameNeedsALetterOrDigit() {
        #expect(reason(Self.make(Self.input(name: "   "))) == "Give the sprite a name.")
        #expect(reason(Self.make(Self.input(name: "!!!"))) == "The name needs a letter or a digit in it.")
    }
}
