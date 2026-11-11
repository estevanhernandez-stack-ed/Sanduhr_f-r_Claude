import Foundation
import Testing
@testable import Sanduhr

/// "Own art, a column per stage" (spec 2.43.0 section 24): the columns, the picks and their sentences, and Apply
/// through the one install path, driven without a panel.
@MainActor
@Suite("Own art, a column per stage (2.43.0)")
struct OwnArtStagesTests {
    private static let id = "three-stage-fixture"

    private static func sheet(_ w: Int, _ h: Int) -> Data { WidgetOwnArtTests.sheet(w, h) }

    /// A three-stage sprite pack (8x8 cells, four per sheet) with stages at 0, 0.4 and 0.75 growth.
    private static func pack() -> [String: Data] {
        let sprite = """
        {"schema_version":1,"frame_w":8,"frame_h":8,"scale":2,"fps":8,
         "animations":{"walk":[0,1],"sit":[0],"nap":[1],"pop":[2],"cheer":[3],"worry":[0],"focus":[1],"sleep":[2],"celebrate":[3]},
         "moods":{"content":"sit","focused":"focus","worried":"worry","exhausted":"sleep","celebrate":"celebrate"},
         "triggers":{"needs_you":"pop","job_passed":"cheer","near_limit":"worry","idle":"nap"},
         "roam":{"edges":["bottom"],"speed_px_s":20},
         "stages":[
          {"id":"egg","name":"Egg","min_growth":0,"sheet":"sprites/egg.png","frame_w":8,"frame_h":8},
          {"id":"kid","name":"Kid","min_growth":0.4,"sheet":"sprites/kid.png","frame_w":8,"frame_h":8},
          {"id":"elder","name":"Elder","min_growth":0.75,"sheet":"sprites/elder.png","frame_w":8,"frame_h":8}]}
        """
        let manifest = """
        {"schema_version":1,"id":"\(id)","name":"Three stage","version":"1.0.0","author":"Fixture Author","license":"MIT",
         "kind":"sprite","sanduhr_min":"3.8.0","description":"A fixture pack for the own art tests.",
         "files":["sprite.json","sprites/egg.png","sprites/elder.png","sprites/kid.png"]}
        """
        var files: [String: Data] = [
            "sprite.json": Data(sprite.utf8), "pack.json": Data(manifest.utf8),
            "LICENSE": WidgetPackFixtures.load("good", "good-sprite-stages")["LICENSE"]!,
        ]
        for s in ["egg", "kid", "elder"] { files["sprites/\(s).png"] = sheet(32, 8) }
        return files
    }

    private static func model(_ files: [String: Data], apply: @escaping ([String: Data]) -> Bool = { _ in true }) throws -> OwnArtStagesModel {
        try #require(OwnArtStagesModel(files: files, apply: apply))
    }

    @Test func columnsFollowMinGrowthWithNamesAndPercentText() throws {
        let m = try Self.model(Self.pack())
        #expect(m.columns.map(\.id) == ["egg", "kid", "elder"])
        #expect(m.columns.map(\.name) == ["Egg", "Kid", "Elder"])
        #expect(m.columns.map(\.growthText) == ["From 0% growth", "From 40% growth", "From 75% growth"])
        #expect(m.columns.allSatisfy { $0.current != nil })
        #expect(!m.canApply)
    }

    @Test func aBadPickShowsItsSentenceAndBlocksApplyUntilClearedOrFixed() throws {
        let m = try Self.model(Self.pack())
        m.pick("egg", Self.sheet(30, 8))
        #expect(m.columns[0].error?.contains("isn't a whole number of 8x8 cells") == true)
        #expect(!m.canApply)
        m.pick("kid", Self.sheet(32, 8))
        #expect(m.columns[1].preview != nil)
        #expect(!m.canApply, "a failed pick elsewhere still blocks")
        m.pick("egg", Self.sheet(8, 8))
        #expect(m.columns[0].error?.contains("needs at least 4") == true)
        m.pick("egg", Data("not a png at all, padded to a length".utf8))
        #expect(m.columns[0].error?.contains("isn't a PNG") == true)
        #expect(m.columns[0].preview == nil)
        m.clear("egg")
        #expect(m.columns[0].error == nil && m.columns[0].picked == nil)
        #expect(m.canApply)
        #expect(m.art.keys.sorted() == ["kid"])
    }

    @Test func applyWithTwoOfThreePickedCallsTheClosureOnceWithThoseTwo() throws {
        var calls: [[String: Data]] = []
        let m = try Self.model(Self.pack(), apply: { calls.append($0); return true })
        m.pick("egg", Self.sheet(64, 8))
        m.pick("elder", Self.sheet(32, 16))
        m.apply()
        #expect(calls.count == 1)
        #expect(calls.first?.keys.sorted() == ["egg", "elder"])
    }

    @Test func applyThroughOwnArtChangesExactlyThosePicksAndBumpsThePatchOnce() throws {
        let files = Self.pack()
        var result: OwnArtResult?
        let m = try Self.model(files, apply: { result = WidgetOwnArt.apply(files, packId: Self.id, art: $0); return result?.ok == true })
        let egg = Self.sheet(64, 8), elder = Self.sheet(32, 16)
        m.pick("egg", egg)
        m.pick("elder", elder)
        m.apply()
        let made = try #require(result)
        #expect(made.ok, "\(made.errors)")
        let out = try #require(made.files)
        #expect(out[WidgetOwnArt.artPath("egg")] == egg)
        #expect(out[WidgetOwnArt.artPath("elder")] == elder)
        #expect(out["sprites/kid.png"] == files["sprites/kid.png"])
        #expect(out["sprites/egg.png"] == nil && out["sprites/elder.png"] == nil)
        #expect(made.manifest?.version == "1.0.1")
    }

    @Test func aOneStageSpriteKeepsTheSinglePanel() {
        #expect(!OwnArtStagesModel.opensSheet(stageCount: 1))
        #expect(OwnArtStagesModel.opensSheet(stageCount: 2))
        #expect(OwnArtStagesModel.opensSheet(stageCount: 3))
        let single = WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: WidgetProposalTests.now).pack!
        #expect(WidgetOwnArt.sprite(single.files)?.stages.count == 1)
    }
}
