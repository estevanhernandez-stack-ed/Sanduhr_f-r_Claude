import Foundation
import Testing
@testable import Sanduhr

/// `propose_sprite_motion`'s app half, the handoff layer (spec 2.39.0 section 19): the state `get_desk` reads (a made-here
/// sprite's frame size and cell count, never an image), the request file's decoder and the result file. The banner,
/// preview and Apply are a later item. Temp folders only.
@Suite("Sprite motion handoff")
@MainActor
struct SpriteMotionTests {
    nonisolated static let now = Date(timeIntervalSince1970: 1_792_692_000)   // 2026-10-22T18:00:00Z
    nonisolated static let stamp = ISO8601.stamp(now)

    /// A temp widgets folder holding one sprite of the user's own art (32x16 px in 8 px cells: 8 cells), one
    /// Claude-made pixel sprite (four 8x8 frames) and one catalog sprite.
    @MainActor final class World {
        let root = WidgetTestRoot()
        let store: WidgetStore
        let handoff: SpriteMotionHandoff
        var answers: [(String, WidgetAnswer)] = []

        init(ready: Bool = true) throws {
            store = root.store()
            let local = WidgetNewSprite.make(.init(png: WidgetOwnArtTests.sheet(32, 16), cellWidth: 8, cellHeight: 8, name: "My Blob",
                                                   scale: nil, tags: ["walk": [0, 1, 2, 3], "sit": [4, 5]]),
                                             installedIds: [], now: SpriteMotionTests.now)
            let lf = try #require(local.files), lm = try #require(local.manifest)
            #expect(store.install(lf, lm, source: .local).ok)
            let claude = try #require(WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: SpriteMotionTests.now).pack)
            #expect(store.install(claude.files, claude.manifest, source: .claude).ok)
            let catalog = WidgetNewSprite.make(.init(png: WidgetOwnArtTests.sheet(16, 8), cellWidth: 8, cellHeight: 8, name: "From Catalog",
                                                     scale: nil, tags: ["walk": [0, 1], "sit": [0]]),
                                               installedIds: [], now: SpriteMotionTests.now)
            let cf = try #require(catalog.files), cm = try #require(catalog.manifest)
            #expect(store.install(cf, cm, source: .catalog).ok)
            let paths = SpriteMotionHandoff.Paths(support: root.url.appendingPathComponent("Support"))
            handoff = SpriteMotionHandoff(paths: paths, store: { [store] in store }, now: { SpriteMotionTests.now })
            handoff.isReady = { ready }
            handoff.onAnswered = { [unowned self] id, a in self.answers.append((id, a)) }
        }

        func send(_ text: String) { handoff.receive(Data(text.utf8)) }

        func resultJson() throws -> [String: Any] {
            let data = try Data(contentsOf: handoff.paths.result)
            return try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        }
    }

    nonisolated private static let animations = """
    {"walk":[0,1],"sit":[2],"nap":[3],"pop":[4],"cheer":[5],"worry":[2],"focus":[2],"sleep":[3],"celebrate":[5]}
    """
    nonisolated private static let moods = """
    {"content":"sit","focused":"focus","worried":"worry","exhausted":"sleep","celebrate":"celebrate"}
    """
    nonisolated private static let triggers = """
    {"needs_you":"pop","job_passed":"cheer","near_limit":"worry","idle":"nap"}
    """

    nonisolated static func request(id: String = "r1", sprite: String = "my-my-blob", at: String = stamp, version: Int = 1,
                        fields: String? = nil, note: String? = "Slower nap.") -> String {
        let body = fields ?? "\"animations\":\(animations),\"moods\":\(moods),\"triggers\":\(triggers),\"fps\":10"
        let noteField = note.map { ",\"note\":\"\($0)\"" } ?? ""
        return "{\"schema_version\":\(version),\"id\":\"\(id)\",\"requested_at\":\"\(at)\",\"sprite_id\":\"\(sprite)\"\(noteField),\"sprite\":{\(body)}}"
    }

    // MARK: - The names

    @Test func theFileNamesAndLimitsMatchTheMcpServer() {
        #expect(SpriteMotionProposal.requestFile == "sprite-motion-request.json")
        #expect(SpriteMotionProposal.resultFile == "sprite-motion-result.json")
        #expect(SpriteMotionProposal.maxRequestBytes == 256 * 1024)
        #expect(SpriteMotionProposal.fields == ["animations", "fps", "scale", "moods", "triggers", "roam"])
        let w = try? World()
        #expect(w?.handoff.paths.request.lastPathComponent == "sprite-motion-request.json")
        #expect(w?.handoff.paths.result.lastPathComponent == "sprite-motion-result.json")
    }

    // MARK: - The state file

    @Test func theStateListsMadeHereSpritesWithFrameSizeAndCellCountOnly() throws {
        let w = try World()
        let sprites = SpriteMotionState.sprites(store: w.store)
        #expect(sprites.map(\.id) == ["my-my-blob", "my-neon-blob"])
        let local = try #require(sprites.first { $0.id == "my-my-blob" })
        #expect((local.frameWidth, local.frameHeight, local.cells) == (8, 8, 8))
        #expect(local.name == "My Blob")
        let pixel = try #require(sprites.first { $0.id == "my-neon-blob" })
        #expect((pixel.frameWidth, pixel.frameHeight, pixel.cells) == (8, 8, 4))
        // The catalog sprite is not made here and is not listed.
        #expect(!sprites.contains { $0.id == "my-from-catalog" })
        for s in sprites { #expect(Set(s.json.keys) == ["id", "name", "frame_w", "frame_h", "cells"]) }
    }

    @Test func theStateFileCarriesTheSpritesAndNoImageData() throws {
        let w = try World()
        let d = UserDefaults(suiteName: "sanduhr.tests.spritemotion.\(UUID().uuidString)")!
        let data = DeskStateExport.stateJson(defaults: d, installedFonts: [], writerVersion: "2.39.0",
                                             sprites: SpriteMotionState.sprites(store: w.store), fontAvailable: { _ in true })
        let root = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let rows = try #require(root["sprites"] as? [[String: Any]])
        #expect(rows.count == 2)
        for row in rows { #expect(Set(row.keys) == ["id", "name", "frame_w", "frame_h", "cells"]) }
        let text = String(decoding: data, as: UTF8.self)
        #expect(!text.contains("iVBOR"))          // no base64 PNG
        #expect(!text.contains("art/sprite.png"))  // no path to the image
        #expect(!text.contains("PNG"))
        #expect(!text.contains("animations"))      // the current tables are not exported
        // With no sprites the list is empty, not absent.
        let none = DeskStateExport.stateJson(defaults: d, installedFonts: [], writerVersion: "2.39.0", fontAvailable: { _ in true })
        let bare = try #require(try JSONSerialization.jsonObject(with: none) as? [String: Any])
        #expect((bare["sprites"] as? [Any])?.isEmpty == true)
    }

    // MARK: - The request

    @Test func aGoodRequestDecodesToAProposalWithTheNewSpriteJson() throws {
        let w = try World()
        let r = SpriteMotionProposal.decode(Data(Self.request().utf8), now: Self.now, store: w.store)
        #expect(r.kind == .proposal)
        #expect(r.id == "r1" && r.spriteId == "my-my-blob" && r.note == "Slower nap.")
        let spriteJson = try #require(r.files?["sprite.json"])
        let sprite = try #require(CompanionSprite.parse(spriteJson))
        #expect(sprite.fps == 10)
        #expect(sprite.frames("walk") == [0, 1])
        #expect(sprite.frames("nap") == [3])
        #expect(sprite.scale == 2)   // left out: unchanged
        #expect(r.files?["art/sprite.png"] != nil)   // the art is the installed one
    }

    @Test func anyFieldBeyondTheSixIsRefusedWithAClearMessage() throws {
        let w = try World()
        for extra in ["image", "pixels", "sheet", "stages", "files", "frame_w", "name"] {
            let body = "\"animations\":\(Self.animations),\"moods\":\(Self.moods),\"triggers\":\(Self.triggers),\"\(extra)\":\"AAAA\""
            let r = SpriteMotionProposal.decode(Data(Self.request(fields: body).utf8), now: Self.now, store: w.store)
            #expect(r.kind == .refused, "\(extra)")
            #expect(r.reasons.first?.message.contains("unknown field") == true)
            #expect(r.reasons.first?.message.contains("art is never changed") == true)
        }
    }

    @Test func aCatalogOrUnknownOrMalformedIdIsRefused() throws {
        let w = try World()
        for id in ["my-from-catalog", "my-nope", "../x", ""] {
            let r = SpriteMotionProposal.decode(Data(Self.request(sprite: id).utf8), now: Self.now, store: w.store)
            #expect(r.kind == .refused, "\(id)")
            #expect(r.reasons.first?.field == "sprite_id")
        }
    }

    @Test func cellIndexesPastTheRealSheetAreRefused() throws {
        let w = try World()
        // 8 cells (0 to 7): cell 8 is out of range.
        let tooFar = Self.animations.replacingOccurrences(of: "\"walk\":[0,1]", with: "\"walk\":[0,8]")
        let body = "\"animations\":\(tooFar),\"moods\":\(Self.moods),\"triggers\":\(Self.triggers)"
        let r = SpriteMotionProposal.decode(Data(Self.request(fields: body).utf8), now: Self.now, store: w.store)
        #expect(r.kind == .refused)
        #expect(r.reasons.contains { $0.message.contains("frame index 8") })
        // The same table on the four-frame pixel sprite: cell 5 is out of range there.
        let r2 = SpriteMotionProposal.decode(Data(Self.request(sprite: "my-neon-blob").utf8), now: Self.now, store: w.store)
        #expect(r2.kind == .refused)
    }

    @Test func missingTablesStaleOrWrongSchemaRequestsAreHandledLikeTheWidgetRequests() throws {
        let w = try World()
        let noMoods = "\"animations\":\(Self.animations),\"triggers\":\(Self.triggers)"
        #expect(SpriteMotionProposal.decode(Data(Self.request(fields: noMoods).utf8), now: Self.now, store: w.store).kind == .refused)
        #expect(SpriteMotionProposal.decode(Data(Self.request(version: 2).utf8), now: Self.now, store: w.store).kind == .refused)
        let old = ISO8601.stamp(Self.now.addingTimeInterval(-601))
        #expect(SpriteMotionProposal.decode(Data(Self.request(at: old).utf8), now: Self.now, store: w.store).kind == .discard)
        #expect(SpriteMotionProposal.decode(Data("not json".utf8), now: Self.now, store: w.store).kind == .discard)
        #expect(SpriteMotionProposal.decode(Data(count: SpriteMotionProposal.maxRequestBytes + 1), now: Self.now, store: w.store).kind == .discard)
        let trademark = Self.animations.replacingOccurrences(of: "\"pop\":[4]", with: "\"claude_pop\":[4]")
        let body = "\"animations\":\(trademark),\"moods\":\(Self.moods),\"triggers\":\(Self.triggers)"
        #expect(SpriteMotionProposal.decode(Data(Self.request(fields: body).utf8), now: Self.now, store: w.store).kind == .refused)
    }

    // MARK: - The handoff

    @Test func aGoodRequestIsHeldAnsweredPreviewingAndWrittenOwnerOnly() throws {
        let w = try World()
        w.send(Self.request())
        #expect(w.handoff.pending?.id == "r1" && w.handoff.pending?.spriteId == "my-my-blob")
        #expect(w.answers.map(\.1.status) == ["previewing"])
        let result = try w.resultJson()
        #expect(result["id"] as? String == "r1")
        let inner = try #require(result["result"] as? [String: Any])
        #expect(inner["status"] as? String == "previewing")
        #expect(inner["pack_id"] as? String == "my-my-blob")
        let mode = try FileManager.default.attributesOfItem(atPath: w.handoff.paths.result.path)[.posixPermissions] as? Int
        #expect(mode == 0o600)
        // Nothing was installed or changed.
        let files = try #require(w.store.readFiles("my-my-blob"))
        let spriteJson = try #require(files["sprite.json"])
        let sprite = try #require(CompanionSprite.parse(spriteJson))
        #expect(sprite.fps == 8)
    }

    @Test func aRefusalIsAnsweredRejectedWithReasons() throws {
        let w = try World()
        w.send(Self.request(sprite: "my-from-catalog"))
        let inner = try #require(try w.resultJson()["result"] as? [String: Any])
        #expect(inner["status"] as? String == "rejected")
        #expect((inner["reasons"] as? [[String: String]])?.first?["field"] == "sprite_id")
        #expect(w.handoff.pending == nil)
    }

    @Test func aNewerRequestReplacesTheHeldOne() throws {
        let w = try World()
        w.send(Self.request(id: "r1"))
        w.send(Self.request(id: "r2"))
        #expect(w.handoff.pending?.id == "r2")
        #expect(w.answers.count == 3)
        #expect(w.answers.first { $0.0 == "r1" && $0.1.status == "rejected" }?.1.reason == "replaced")
        w.handoff.decline()
        #expect(w.handoff.pending == nil)
        #expect(w.answers.last?.0 == "r2" && w.answers.last?.1.reason == "dismissed")
    }

    @Test func withNothingToShowItAnswersNotReadyAndAfterAResetToo() throws {
        let w = try World(ready: false)
        w.send(Self.request())
        #expect(w.handoff.pending == nil)
        #expect(w.answers.first?.1.reason == "not_ready")
        let r = try World()
        ResetGate.$testOverride.withValue(true) { r.send(Self.request()) }
        #expect(r.handoff.pending == nil)
        #expect(r.answers.first?.1.reason == "not_ready")
    }

    @Test func theRequestFileIsReadAndRemovedAtOnce() throws {
        let w = try World()
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        try Data(Self.request().utf8).write(to: w.handoff.paths.request)
        w.handoff.check()
        #expect(!FileManager.default.fileExists(atPath: w.handoff.paths.request.path))
        #expect(w.handoff.pending?.id == "r1")
    }
}
