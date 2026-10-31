import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// A store the motion review reads and rewrites, held in memory.
@MainActor
final class FakeMotionStore: SpriteMotionStoring {
    var packs: [String: [String: Data]]
    var madeHere: Set<String>
    var installs: [(files: [String: Data], manifest: PackManifest)] = []
    var failWith: String?

    init(packs: [String: [String: Data]], madeHere: Set<String>) {
        self.packs = packs
        self.madeHere = madeHere
    }

    func isMadeHere(_ id: String) -> Bool { madeHere.contains(id) }
    func files(_ id: String) -> [String: Data]? { packs[id] }
    func reinstall(_ files: [String: Data], _ manifest: PackManifest) -> WidgetStoreResult {
        if let failWith { return .failed(failWith) }
        installs.append((files, manifest))
        packs[manifest.id] = files
        return .done
    }
}

/// The Desk side of a review, recorded.
@MainActor
final class FakeMotionHost: WidgetPreviewHost {
    var deskRunning = true
    var deskBusy = false
    var shown: [WidgetDeskPreview] = []
    var ended = 0
    func show(_ preview: WidgetDeskPreview) { shown.append(preview) }
    func end() { ended += 1 }
}

/// `propose_sprite_motion`'s review (spec 2.39.0 section 19): the summary, Apply, Decline, expiry, Preview on the Desk
/// and a sprite uninstalled meanwhile. Temp folders and fakes only.
@Suite("Sprite motion review")
@MainActor
struct SpriteMotionReviewTests {
    typealias World = SpriteMotionTests.World
    static let id = "my-my-blob"

    private func rig() throws -> (World, FakeMotionStore, FakeMotionHost) {
        let w = try World()
        let store = FakeMotionStore(packs: [Self.id: try #require(w.store.readFiles(Self.id))], madeHere: [Self.id])
        let host = FakeMotionHost()
        w.handoff.motionStore = { store }
        w.handoff.host = host
        return (w, store, host)
    }

    private func sprite(_ files: [String: Data]?) throws -> CompanionSprite {
        let data = try #require(files?["sprite.json"])
        return try #require(CompanionSprite.parse(data))
    }

    private func version(_ files: [String: Data]?) throws -> String? {
        let data = try #require(files?["pack.json"])
        let scalars = try #require(PackJson.decodeUTF8([UInt8](data)))
        guard case .success(let v) = PackJson.parse(scalars) else { return nil }
        return v.object?["version"]?.string
    }

    // MARK: - Apply

    @Test func applyRewritesTheMotionBumpsThePatchAndLeavesTheArtAlone() throws {
        let (w, store, _) = try rig()
        let before = try #require(store.packs[Self.id])
        w.send(SpriteMotionTests.request())
        w.handoff.apply()
        #expect(w.answers.map(\.1.status) == ["previewing", "applied"])
        #expect(w.answers.last?.1.packId == Self.id)
        #expect(w.handoff.pending == nil)
        let install = try #require(store.installs.first)
        #expect(store.installs.count == 1)
        #expect(try sprite(install.files).fps == 10)
        #expect(try sprite(install.files).animations["nap"] == [3])
        #expect(try version(before) == "1.0.0")
        #expect(try version(install.files) == "1.0.1")
        #expect(install.manifest.version == "1.0.1")
        for (path, bytes) in before where path != "sprite.json" && path != "pack.json" {
            #expect(install.files[path] == bytes, "\(path) must be byte-equal")
        }
        let result = try #require(try w.resultJson()["result"] as? [String: Any])
        #expect(result["status"] as? String == "applied")
    }

    @Test func applyInstallsIntoTheRealStoreAsLocalAndKeepsTheArt() throws {
        let w = try World()
        let before = try #require(w.store.readFiles(Self.id))
        w.send(SpriteMotionTests.request())
        w.handoff.apply()
        #expect(w.answers.last?.1.status == "applied")
        let after = try #require(w.store.readFiles(Self.id))
        #expect(try sprite(after).fps == 10)
        #expect(try version(after) == "1.0.1")
        for (path, bytes) in before where path != "sprite.json" && path != "pack.json" {
            #expect(after[path] == bytes)
        }
        let row = try #require(w.store.load().find(Self.id))
        #expect(row.source == .local && row.version == "1.0.1")
        // A second pack is untouched.
        #expect(try sprite(w.store.readFiles("my-from-catalog")).fps == 8)
    }

    @Test func aReinstallKeepsTheSourceTheSpriteHad() throws {
        let w = try World()
        let claude = try #require(w.store.load().packs.first { $0.source == .claude })
        let files = try #require(w.store.readFiles(claude.id))
        let manifest = try #require(PackValidator.validate(files, folderName: claude.id, strict: true).manifest)
        #expect(w.store.reinstall(files, manifest).ok)
        #expect(w.store.load().find(claude.id)?.source == .claude)
    }

    @Test func anInstallFailureIsAnsweredWithItsSentence() throws {
        let (w, store, _) = try rig()
        store.failWith = "disk is full"
        w.send(SpriteMotionTests.request())
        w.handoff.apply()
        let a = try #require(w.answers.last?.1)
        #expect(a.status == "rejected" && a.reason == "install_failed")
        #expect(a.reasons?.first?.message == "disk is full")
        #expect(store.installs.isEmpty)
    }

    // MARK: - Uninstalled meanwhile

    @Test func aSpriteUninstalledMeanwhileFailsApplyWithASentence() throws {
        let (w, store, _) = try rig()
        w.send(SpriteMotionTests.request())
        store.packs[Self.id] = nil
        store.madeHere = []
        w.handoff.apply()
        let a = try #require(w.answers.last?.1)
        #expect(a.status == "rejected" && a.reason == "install_failed")
        #expect(a.reasons?.first?.message == SpriteMotionReviewCopy.gone)
        #expect(a.packId == Self.id)
        #expect(w.handoff.pending == nil)
        #expect(store.installs.isEmpty)
    }

    @Test func aRealUninstallMeanwhileDoesNotBringTheSpriteBack() throws {
        let w = try World()
        w.send(SpriteMotionTests.request())
        #expect(w.store.uninstall(Self.id).ok)
        w.handoff.apply()
        #expect(w.answers.last?.1.reason == "install_failed")
        #expect(w.store.readFiles(Self.id) == nil)
    }

    @Test func aPackThatNoLongerFitsIsRefusedAndNothingChanges() throws {
        let (w, store, _) = try rig()
        w.send(SpriteMotionTests.request())
        // The art shrank to 2 cells while the motion waited: cell 5 no longer exists.
        var files = try #require(store.packs[Self.id])
        let narrow = WidgetOwnArtTests.sheet(16, 8)
        let artPath = try #require(files.keys.first { $0.hasSuffix(".png") })
        files[artPath] = narrow
        store.packs[Self.id] = files
        w.handoff.apply()
        let a = try #require(w.answers.last?.1)
        #expect(a.status == "rejected" && a.reason == "install_failed")
        #expect(store.installs.isEmpty)
    }

    // MARK: - Decline

    @Test func declineAnswersDismissedAndChangesNothing() throws {
        let (w, store, host) = try rig()
        let before = store.packs
        w.send(SpriteMotionTests.request())
        w.handoff.decline()
        let a = try #require(w.answers.last?.1)
        #expect(a.status == "rejected" && a.reason == "dismissed" && a.packId == Self.id)
        #expect(w.handoff.pending == nil)
        #expect(store.installs.isEmpty && store.packs == before)
        #expect(host.shown.isEmpty)
        // Nothing waits any more: a second press does nothing.
        w.handoff.decline()
        w.handoff.apply()
        #expect(w.answers.count == 2)
    }

    // MARK: - Expiry and replacement

    @Test func aRequestOlderThanTenMinutesIsDroppedUnanswered() throws {
        let (w, _, _) = try rig()
        let old = ISO8601.stamp(SpriteMotionTests.now.addingTimeInterval(-601))
        w.send(SpriteMotionTests.request(at: old))
        #expect(w.handoff.pending == nil && w.answers.isEmpty)
        let fresh = ISO8601.stamp(SpriteMotionTests.now.addingTimeInterval(-599))
        w.send(SpriteMotionTests.request(at: fresh))
        #expect(w.handoff.pending != nil)
    }

    @Test func aNewerRequestTakesDownTheOlderOnesPreviewAndTellsTheBanner() throws {
        let (w, _, host) = try rig()
        var decided: [String] = []
        w.handoff.onDecided = { decided.append($0) }
        w.send(SpriteMotionTests.request(id: "r1"))
        #expect(w.handoff.previewOnDesk())
        w.send(SpriteMotionTests.request(id: "r2"))
        #expect(w.handoff.pending?.id == "r2")
        #expect(decided == ["r1"])
        #expect(host.ended == 1 && !w.handoff.previewing)
        #expect(w.answers.first { $0.0 == "r1" && $0.1.reason == "replaced" } != nil)
    }

    // MARK: - Preview

    @Test func previewDrawsTheProposedMotionFromMemoryAndWritesNothing() throws {
        let (w, store, host) = try rig()
        let before = store.packs
        let listing = try FileManager.default.subpathsOfDirectory(atPath: w.root.widgets.path).sorted()
        let registry = try Data(contentsOf: w.root.widgets.appendingPathComponent("widgets.json"))
        w.send(SpriteMotionTests.request())
        #expect(w.handoff.previewOnDesk())
        #expect(w.handoff.previewing && w.handoff.model?.previewing == true)
        let preview = try #require(host.shown.last?.companion)
        #expect(preview.packId == Self.id)
        #expect(preview.sprite.fps == 10)
        let stage = try #require(preview.sprite.stages.first)
        #expect(preview.sheet(for: stage)?.width == 32)
        #expect(host.shown.last?.ink == nil)
        // Nothing written: not the store, not the folder, not the registry. The result file holds only `previewing`.
        #expect(store.installs.isEmpty && store.packs == before)
        #expect(try FileManager.default.subpathsOfDirectory(atPath: w.root.widgets.path).sorted() == listing)
        #expect(try Data(contentsOf: w.root.widgets.appendingPathComponent("widgets.json")) == registry)
        let inner = try #require(try w.resultJson()["result"] as? [String: Any])
        #expect(inner["status"] as? String == "previewing")
        w.handoff.endPreview()
        #expect(host.ended == 1 && !w.handoff.previewing)
        #expect(w.handoff.pending != nil)
    }

    @Test func previewWorksForAPixelSpriteToo() throws {
        let w = try World()
        let row = try #require(w.store.load().packs.first { $0.source == .claude })
        let files = try #require(w.store.readFiles(row.id))
        #expect(CompanionPreview.make(packId: row.id, files: files) != nil)
    }

    @Test func previewSaysWhyWhenTheDeskIsOffAndApplyEndsAPreview() throws {
        let (w, _, host) = try rig()
        host.deskRunning = false
        w.send(SpriteMotionTests.request())
        #expect(w.handoff.model?.deskOff == true && w.handoff.model?.previewHint == WidgetReviewCopy.deskOff)
        #expect(!w.handoff.previewOnDesk())
        #expect(w.handoff.previewNote == WidgetReviewCopy.deskOff && host.shown.isEmpty)
        host.deskRunning = true
        #expect(w.handoff.previewOnDesk())
        w.handoff.apply()
        #expect(host.ended == 1 && !w.handoff.previewing)
    }

    // MARK: - The banner's model and the pure pieces

    @Test func theBannerNamesTheSpriteTheNoteAndWhatChanges() throws {
        let (w, _, _) = try rig()
        var shown: [SpriteMotionReviewModel] = []
        w.handoff.onPending = { shown.append($0) }
        w.send(SpriteMotionTests.request(note: "Slower nap."))
        let m = try #require(w.handoff.model)
        #expect(shown == [m])
        #expect(m.name == "My Blob" && m.note == "Slower nap.")
        #expect(m.heading == "Claude suggests a motion · My Blob")
        #expect(m.changes.contains("Speed: 8 fps to 10 fps"))
        #expect(m.changes.contains { $0.hasPrefix("9 animations: celebrate 1 frame, cheer 1 frame") })
        #expect(m.changes.last == SpriteMotionReviewCopy.unchangedArt)
    }

    @Test func thePatchBumpOnlyTouchesTheLastNumber() {
        #expect(SpriteMotionReview.bumpedPatch("1.0.0") == "1.0.1")
        #expect(SpriteMotionReview.bumpedPatch("2.4.9") == "2.4.10")
        #expect(SpriteMotionReview.bumpedPatch("1.0") == nil)
        #expect(SpriteMotionReview.bumpedPatch("1.0.x") == nil)
        #expect(SpriteMotionReview.bumpedPatch("") == nil)
    }
}
