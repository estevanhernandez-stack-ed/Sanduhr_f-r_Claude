import Foundation
import Testing
@testable import Sanduhr

/// Settings, Widgets: "Use my own art..." through `WidgetsModel` (the panel is only the source of bytes), and the
/// page following `.widgetPacksDidChange`.
@Suite("Widgets model, own art and refresh")
@MainActor
struct WidgetsOwnArtModelTests {
    private func model(_ root: WidgetTestRoot, _ scratch: WidgetScratchDefaults) -> WidgetsModel {
        WidgetsModel(store: root.store(), defaults: scratch.defaults, makeClient: { WidgetCatalogClient(fetch: { _, _ in nil }, marker: $0) },
                     changed: {})
    }

    private func claudeSprite() -> WidgetProposalPack {
        WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: WidgetProposalTests.now).pack!
    }

    private func installClaudeSprite(_ root: WidgetTestRoot, source: WidgetSource = .claude) -> WidgetProposalPack {
        let pack = claudeSprite()
        #expect(root.store().install(pack.files, pack.manifest, source: source).ok)
        return pack
    }

    @Test func aFailingCheckSurfacesItsSentenceAndChangesNothing() {
        let root = WidgetTestRoot()
        let pack = installClaudeSprite(root)
        let m = model(root, WidgetScratchDefaults())
        let before = root.store().readFiles(pack.id)
        // 30 pixels wide is not a whole number of 8x8 cells.
        let why = m.checkOwnArt(id: pack.id, stageId: "only", data: WidgetOwnArtTests.sheet(30, 8))
        #expect(why?.contains("isn't a whole number of 8x8 cells") == true && why?.hasSuffix("is unchanged.") == true)
        #expect(m.ownArtStatus == why)
        #expect(!m.useOwnArt(id: pack.id, art: ["only": WidgetOwnArtTests.sheet(30, 8)]))
        #expect(m.ownArtStatus?.contains("isn't a whole number") == true)
        #expect(root.store().readFiles(pack.id) == before)
        #expect(m.checkOwnArt(id: pack.id, stageId: "only", data: nil)?.contains("couldn't be read") == true)
        #expect(m.checkOwnArt(id: pack.id, stageId: "only", data: WidgetOwnArtTests.sheet(32, 8)) == nil && m.ownArtStatus == nil)
    }

    @Test func successReinstallsAsLocalWithTheBumpedPatchAndTheArtOnDisk() throws {
        let root = WidgetTestRoot()
        let pack = installClaudeSprite(root)
        let m = model(root, WidgetScratchDefaults())
        let row = try #require(m.installed.first)
        #expect(m.canUseOwnArt(row))
        let prompts = try #require(m.ownArtPrompts(for: pack.id))
        #expect(prompts.map(\.stageId) == ["only"] && prompts[0].title.hasPrefix("Your own art for "))

        #expect(m.useOwnArt(id: pack.id, art: ["only": WidgetOwnArtTests.sheet(32, 8)]))
        #expect(m.ownArtStatus?.hasSuffix("now uses your own art. It stays on this Mac.") == true)
        let entry = try #require(root.store().load().find(pack.id))
        #expect(entry.source == .local && entry.version == "1.0.1")
        #expect(m.installed.first?.version == "1.0.1" && m.installed.first?.madeHere == true)
        #expect(root.store().readFiles(pack.id)?["art/sprite.png"] != nil)
    }

    @Test func aCatalogPackNeverOffersOwnArt() throws {
        let root = WidgetTestRoot()
        let (files, manifest) = StoreFixtures.valid("good", "good-sprite")
        #expect(root.store().install(files, manifest, source: .catalog).ok)
        let m = model(root, WidgetScratchDefaults())
        let row = try #require(m.installed.first)
        #expect(!m.canUseOwnArt(row))
        #expect(m.ownArtPrompts(for: row.id) == nil)
        #expect(!m.useOwnArt(id: row.id, art: ["only": WidgetOwnArtTests.sheet(32, 8)]))
        #expect(root.store().load().find(row.id)?.source == .catalog)
        #expect(root.store().load().find(row.id)?.version == row.version)
    }

    @Test func aLocalSpriteOffersOwnArtToo() throws {
        let root = WidgetTestRoot()
        _ = installClaudeSprite(root, source: .local)
        let m = model(root, WidgetScratchDefaults())
        #expect(m.canUseOwnArt(try #require(m.installed.first)))
    }

    @Test func thePageFollowsWidgetPacksDidChange() {
        let root = WidgetTestRoot()
        let scratch = WidgetScratchDefaults()
        let m = model(root, scratch)
        #expect(m.installed.isEmpty)
        // Installed behind the model's back (the review banner's Apply), then announced.
        let pack = installClaudeSprite(root)
        #expect(m.installed.isEmpty)
        NotificationCenter.default.post(name: .widgetPacksDidChange, object: nil)
        #expect(m.installed.map(\.id) == [pack.id])
        // Make it the companion saved a pick in the Desk's defaults: the model reads it again.
        CompanionSettings(pack: pack.id).save(scratch.defaults)
        NotificationCenter.default.post(name: .widgetPacksDidChange, object: nil)
        #expect(m.companion.pack == pack.id)
    }

    @Test func thePreviewHintExplainsADisabledPreview() {
        let sprite = claudeSprite()
        let on = WidgetReview.model(id: "r", pack: sprite, note: nil, deskOn: true)
        #expect(on.previewHint == nil)
        let off = WidgetReview.model(id: "r", pack: sprite, note: nil, deskOn: false)
        #expect(off.previewHint == WidgetReviewCopy.deskOff)
        let showing = WidgetReview.model(id: "r", pack: sprite, note: nil, deskOn: false, previewing: true)
        #expect(showing.previewHint == nil)
    }
}
