import Foundation
import Testing
@testable import Sanduhr

/// Widgets made with Claude, the app side (the review): a request is checked again, held in memory and answered
/// `previewing`; Preview on the Desk draws a sprite as the companion or a palette as the ink without touching what is
/// saved; Apply installs it as `claude` and can make it the companion; Dismiss writes nothing. Ported from Windows'
/// WidgetReviewServiceTests, WidgetHandoff and WidgetReview cases. Scratch folders, an isolated defaults suite, an
/// injected clock and a fake Desk only: never the real Sanduhr folders or a Desk window.
@MainActor
@Suite("Widget review")
struct WidgetReviewTests {
    nonisolated static let now = WidgetProposalTests.now

    @MainActor
    final class FakeHost: WidgetPreviewHost {
        var deskRunning = true
        var deskBusy = false
        var calls: [String] = []
        var shown: WidgetDeskPreview?
        func show(_ preview: WidgetDeskPreview) { calls.append("show"); shown = preview }
        func end() { calls.append("end"); shown = nil }
    }

    /// One scratch world: a support folder, a widgets folder, a defaults suite and the handoff over them.
    @MainActor
    final class World {
        let root = WidgetTestRoot()
        let defaults: UserDefaults
        let suite = "sanduhr-review-" + UUID().uuidString.prefix(8)
        let host = FakeHost()
        let handoff: WidgetProposalHandoff
        let store: WidgetStore
        var answers: [(String, WidgetAnswer)] = []
        var installed: [String] = []
        var decided: [String] = []
        var suggested: [WidgetReviewModel] = []

        init(host wired: Bool = true, now: Date = WidgetReviewTests.now) {
            defaults = UserDefaults(suiteName: suite)!
            let store = root.store()
            self.store = store
            let support = root.url.appendingPathComponent("support", isDirectory: true)
            handoff = WidgetProposalHandoff(paths: .init(support: support), store: { store }, defaults: defaults, now: { now })
            if wired { handoff.host = host }
            handoff.onAnswered = { [unowned self] in answers.append(($0, $1)) }
            handoff.onInstalled = { [unowned self] in installed.append($0) }
            handoff.onDecided = { [unowned self] in decided.append($0) }
            handoff.onSuggestion = { [unowned self] in suggested.append($0) }
        }

        deinit { defaults.removePersistentDomain(forName: suite) }

        var requestFile: URL { handoff.paths.request }
        var resultFile: URL { handoff.paths.result }

        func send(_ id: String, _ args: PackObject, note: String? = "made for you", at: Date = WidgetReviewTests.now) {
            let pack = WidgetProposal.build(args: args, now: WidgetReviewTests.now).pack!
            handoff.receive(request(id, pack, note: note, at: at))
        }

        func request(_ id: String, _ pack: WidgetProposalPack, note: String? = nil, at: Date = WidgetReviewTests.now) -> Data {
            Data(WidgetJson.compact(.object(WidgetProposal.requestJson(requestId: id, pack: pack, note: note, now: at))).utf8)
        }

        func result() throws -> PackValue {
            TJ.text(try Data(contentsOf: resultFile))
        }

        func status() throws -> String? { TJ.get(try result(), ["result", "status"])?.string }
        func reason() throws -> String? { TJ.get(try result(), ["result", "reason"])?.string }

        var registry: WidgetRegistry { store.load() }
    }

    // MARK: - Held, answered, not installed

    @Test func aSpriteIsHeldAndAnsweredPreviewingWithNothingInstalledOrDrawn() throws {
        let w = World()
        w.send("w1", WidgetProposalTests.spriteArgs(id: "blob", name: "Blob"))
        #expect(try w.status() == "previewing")
        #expect(try TJ.get(w.result(), ["result", "pack_id"])?.string == "my-blob")
        #expect(try TJ.get(w.result(), ["id"])?.string == "w1")
        #expect(w.host.calls.isEmpty, "nothing is drawn until the user asks")
        let p = try #require(w.handoff.pending)
        #expect(p.id == "w1" && p.pack.id == "my-blob")
        #expect(w.registry.packs.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: w.store.packDir("my-blob").path))
        #expect(!FileManager.default.fileExists(atPath: w.store.registryFile.path))
        #expect(w.suggested.map(\.id) == ["w1"])
        let m = try #require(w.handoff.model)
        #expect(m.heading == "Claude suggests a widget · Blob")
        #expect(m.option == "Make it the companion")
        #expect(m.note == "made for you")
        #expect(m.kind == "sprite" && m.canPreview && !m.deskOff && !m.previewing)
        #expect(m.files == ["LICENSE", "pack.json", "sheet.json", "sprite.json"])
        #expect(m.license == "CC0-1.0")
    }

    @Test func theRequestFileIsTakenAndTheResultIsOwnerOnly() throws {
        let w = World()
        let pack = WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        try w.request("f1", pack).write(to: w.requestFile)
        w.handoff.check()
        #expect(!FileManager.default.fileExists(atPath: w.requestFile.path))
        #expect(try w.status() == "previewing")
        let mode = try FileManager.default.attributesOfItem(atPath: w.resultFile.path)[.posixPermissions] as? Int
        #expect(mode == 0o600)
        #expect(w.handoff.pending?.id == "f1")
    }

    @Test func theHandoffWatchesTheSupportFolder() throws {
        let w = World()
        let watch = HandoffWatch(dir: w.handoff.paths.support)
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        let pack = WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!
        try w.request("f2", pack).write(to: w.requestFile)
        w.handoff.start(watch: watch)   // takes a request already waiting
        #expect(w.handoff.pending?.id == "f2")
        watch.stop()
    }

    @Test func anExpiredRequestIsDroppedUnansweredAndTheFileGoes() throws {
        let w = World(now: Self.now.addingTimeInterval(11 * 60))
        let pack = WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        try w.request("old", pack, at: Self.now).write(to: w.requestFile)
        w.handoff.check()
        #expect(!FileManager.default.fileExists(atPath: w.requestFile.path))
        #expect(!FileManager.default.fileExists(atPath: w.resultFile.path))
        #expect(w.handoff.pending == nil && w.answers.isEmpty && w.suggested.isEmpty)
        // Just inside the window it is taken.
        let fresh = World(now: Self.now.addingTimeInterval(599))
        fresh.send("new", WidgetProposalTests.paletteArgs())
        #expect(fresh.handoff.pending?.id == "new")
    }

    @Test func aTamperedRequestIsRefusedWithoutBeingHeld() throws {
        let w = World()
        let pack = WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: Self.now).pack!
        let request = WidgetProposal.requestJson(requestId: "t1", pack: pack, note: nil, now: Self.now)
        // The writer escapes a quote as \u0022 (System.Text.Json's way).
        let text = WidgetJson.compact(.object(request)).replacingOccurrences(of: "\\u0022walk\\u0022", with: "\\u0022anthropic_walk\\u0022")
        #expect(text != WidgetJson.compact(.object(request)), "the edit landed")
        w.handoff.receive(Data(text.utf8))
        #expect(try w.status() == "rejected")
        #expect(w.handoff.pending == nil)
        #expect(w.host.calls.isEmpty)
        #expect(w.suggested.isEmpty)
    }

    @Test func aThemeOrFontKindIsNeverHeld() throws {
        let w = World()
        let pack = WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!
        var files = pack.files
        files["theme.json"] = Data("{}".utf8)
        let obj = WidgetProposal.requestJson(requestId: "t2", pack: WidgetProposalPack(files: files, manifest: pack.manifest), note: nil, now: Self.now)
        w.handoff.receive(Data(WidgetJson.compact(.object(obj)).utf8))
        #expect(try w.status() == "rejected")
        #expect(w.handoff.pending == nil)
    }

    // MARK: - Preview

    @Test func previewingASpriteDrawsItFromMemoryAndLeavesTheSavedCompanionAndFolderAlone() throws {
        let w = World()
        CompanionSettings(pack: "hourglass").save(w.defaults)
        w.defaults.set("ffffff", forKey: "inkColor")
        w.send("p1", WidgetProposalTests.spriteArgs(id: "blob"))
        #expect(w.handoff.previewOnDesk())
        #expect(w.host.calls == ["show"])
        #expect(w.handoff.previewing)
        let preview = try #require(w.host.shown?.companion)
        #expect(preview.packId == "my-blob")
        #expect(w.host.shown?.ink == nil)
        let stage = try #require(preview.sprite.stages.first)
        let sheet = try #require(preview.sheet(for: stage))
        #expect(sheet.width % stage.frameWidth == 0 && sheet.height % stage.frameHeight == 0)
        #expect(sheet.width >= stage.frameWidth * 4 || sheet.height >= stage.frameHeight)
        #expect(w.handoff.model?.previewing == true)
        // What was saved is what was there; nothing reached the widgets folder.
        #expect(CompanionSettings.load(w.defaults).pack == "hourglass")
        #expect(w.defaults.string(forKey: "inkColor") == "ffffff")
        #expect(w.defaults.string(forKey: "layout") == nil)
        #expect(!FileManager.default.fileExists(atPath: w.root.widgets.path))
        #expect(w.registry.packs.isEmpty)
    }

    @Test func previewingAPaletteHandsTheDeskItsFirstPalettesInk() throws {
        let w = World()
        w.send("p2", WidgetProposalTests.paletteArgs())
        #expect(w.handoff.previewOnDesk())
        #expect(w.host.shown?.ink == "ff2a6d,d16ba5,05d9e8")
        #expect(w.host.shown?.companion == nil)
        #expect(w.defaults.string(forKey: "inkColor") == nil)
        #expect(w.registry.packs.isEmpty)
    }

    @Test func anEffectsPackHasNothingToPreviewAndAppliesAnyway() throws {
        let w = World()
        w.send("p3", WidgetProposalTests.effectsArgs())
        #expect(w.handoff.model?.canPreview == false)
        #expect(!w.handoff.previewOnDesk())
        #expect(w.handoff.previewNote == WidgetReviewCopy.noPreview)
        #expect(w.host.calls.isEmpty)
        w.handoff.apply()
        #expect(try w.status() == "applied")
        #expect(try TJ.get(w.result(), ["result", "companion"]) == nil)
        #expect(w.registry.find("my-calm")?.source == .claude)
    }

    @Test func withTheDeskOffThereIsNothingToShowAndNothingBreaks() throws {
        let w = World()
        w.host.deskRunning = false
        w.send("p4", WidgetProposalTests.spriteArgs())
        #expect(w.handoff.model?.deskOff == true)
        #expect(!w.handoff.previewOnDesk())
        #expect(w.handoff.previewNote == WidgetReviewCopy.deskOff)
        #expect(!w.handoff.previewing && w.host.calls.isEmpty)
        #expect(w.handoff.pending != nil)
    }

    @Test func endingThePreviewGivesTheDeskBackAndKeepsTheProposal() throws {
        let w = World()
        w.send("p5", WidgetProposalTests.paletteArgs())
        w.handoff.previewOnDesk()
        w.handoff.endPreview()
        #expect(w.host.calls == ["show", "end"])
        #expect(!w.handoff.previewing && w.handoff.pending != nil)
        w.handoff.endPreview()   // nothing to end
        #expect(w.host.calls == ["show", "end"])
    }

    // MARK: - Apply

    @Test func applyInstallsAsClaudeMadeAndMakesItTheCompanion() throws {
        let w = World()
        w.send("a1", WidgetProposalTests.spriteArgs(id: "blob"))
        w.handoff.previewOnDesk()
        w.handoff.apply(makeCompanion: true)
        #expect(try w.status() == "applied")
        #expect(try TJ.get(w.result(), ["result", "companion"])?.bool == true)
        #expect(try TJ.get(w.result(), ["result", "pack_id"])?.string == "my-blob")
        let row = try #require(w.registry.find("my-blob"))
        #expect(row.source == .claude && row.madeHere)
        #expect(FileManager.default.fileExists(atPath: w.store.rasterPath("my-blob", "sheet.json").path))
        #expect(CompanionSettings.load(w.defaults).pack == "my-blob")
        #expect(DeskArrangement(w.defaults.string(forKey: "layout") ?? "").placement("companion") != nil)
        #expect(w.host.calls == ["show", "end"])
        #expect(w.installed == ["my-blob"] && w.decided == ["a1"])
        #expect(w.handoff.pending == nil && !w.handoff.previewing)
    }

    @Test func applyWithoutTheCompanionChoiceLeavesTheCompanionAlone() throws {
        let w = World()
        CompanionSettings(pack: "hourglass").save(w.defaults)
        w.send("a2", WidgetProposalTests.spriteArgs(id: "blob"))
        w.handoff.apply(makeCompanion: false)
        #expect(try TJ.get(w.result(), ["result", "companion"])?.bool == false)
        #expect(CompanionSettings.load(w.defaults).pack == "hourglass")
        #expect(w.defaults.string(forKey: "layout") == nil)
        #expect(w.registry.find("my-blob") != nil)
    }

    @Test func aPlacedCompanionPieceKeepsItsPlace() throws {
        let w = World()
        w.defaults.set("clock:bl companion:tr:1.2", forKey: "layout")
        w.send("a3", WidgetProposalTests.spriteArgs(id: "blob"))
        w.handoff.apply(makeCompanion: true)
        #expect(w.defaults.string(forKey: "layout") == "clock:bl companion:tr:1.2")
    }

    @Test func applyingAPaletteInstallsItAndNeverTouchesTheCompanion() throws {
        let w = World()
        w.send("a4", WidgetProposalTests.paletteArgs())
        w.handoff.apply(makeCompanion: true)
        #expect(try w.status() == "applied")
        #expect(try TJ.get(w.result(), ["result", "companion"]) == nil)
        #expect(CompanionSettings.load(w.defaults).pack == nil)
        #expect(w.defaults.string(forKey: "layout") == nil)
        #expect(w.registry.find("my-dusk")?.source == .claude)
    }

    @Test func aFailedInstallAnswersInstallFailedAndChangesNothing() throws {
        let w = World()
        struct Boom: Error {}
        w.store.beforeSwap = { throw Boom() }
        w.send("a5", WidgetProposalTests.spriteArgs(id: "blob"))
        w.handoff.previewOnDesk()
        w.handoff.apply(makeCompanion: true)
        #expect(try w.status() == "rejected")
        #expect(try w.reason() == "install_failed")
        #expect(w.registry.packs.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: w.store.packDir("my-blob").path))
        #expect(CompanionSettings.load(w.defaults).pack == nil)
        #expect(w.defaults.string(forKey: "layout") == nil)
        #expect(w.installed.isEmpty)
        #expect(w.host.calls == ["show", "end"], "the preview still comes down")
        #expect(w.handoff.pending == nil)
    }

    @Test func aMadeHerePackWithTheSameIdFromAnotherSourceIsNotOverwritten() throws {
        let w = World()
        let pack = WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!
        #expect(w.store.install(pack.files, pack.manifest, source: .catalog).ok)
        w.send("a6", WidgetProposalTests.paletteArgs())
        w.handoff.apply()
        #expect(try w.reason() == "install_failed")
        #expect(w.registry.find("my-dusk")?.source == .catalog)
    }

    @Test func noSettingLetsClaudeSkipApplyForAWidget() throws {
        let w = World()
        w.defaults.set(true, forKey: ThemeProposalHandoff.directKey)
        UserDefaults.standard.set(true, forKey: ThemeProposalHandoff.directKey)
        defer { UserDefaults.standard.removeObject(forKey: ThemeProposalHandoff.directKey) }
        w.send("d1", WidgetProposalTests.paletteArgs())
        #expect(try w.status() == "previewing")
        #expect(w.registry.packs.isEmpty)
        #expect(w.handoff.pending != nil)
    }

    // MARK: - Dismiss, replaced, busy, not ready

    @Test func dismissInstallsNothingTakesThePreviewDownAndAnswersDismissed() throws {
        let w = World()
        CompanionSettings(pack: "hourglass").save(w.defaults)
        w.send("x1", WidgetProposalTests.spriteArgs(id: "blob"))
        w.handoff.previewOnDesk()
        w.handoff.dismiss()
        #expect(try w.status() == "rejected")
        #expect(try w.reason() == "dismissed")
        #expect(w.registry.packs.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: w.store.registryFile.path))
        #expect(CompanionSettings.load(w.defaults).pack == "hourglass")
        #expect(w.host.calls == ["show", "end"])
        #expect(w.handoff.pending == nil && !w.handoff.previewing)
        #expect(w.decided == ["x1"])
        w.handoff.dismiss()   // nothing left to dismiss: no second answer
        #expect(w.answers.count == 2)   // previewing, dismissed
    }

    @Test func aNewerProposalAnswersTheOlderReplacedAndTakesItsPreviewDown() throws {
        let w = World()
        w.send("r1", WidgetProposalTests.spriteArgs(id: "blob"))
        w.handoff.previewOnDesk()
        w.send("r2", WidgetProposalTests.paletteArgs())
        #expect(w.answers.map(\.0) == ["r1", "r1", "r2"])
        #expect(w.answers[1].1.status == "rejected" && w.answers[1].1.reason == "replaced")
        #expect(w.answers[1].1.packId == "my-blob")
        #expect(w.answers[2].1.status == "previewing")
        #expect(w.host.calls == ["show", "end"])
        #expect(!w.handoff.previewing)
        #expect(w.handoff.pending?.id == "r2" && w.handoff.pending?.pack.id == "my-dusk")
        #expect(w.decided == ["r1"])
        w.handoff.dismiss()   // acts on what is waiting now
        #expect(w.handoff.pending == nil)
        #expect(w.answers.last?.0 == "r2")
    }

    @Test func withNoDeskWiredTheAnswerIsNotReadyAndAfterAResetToo() throws {
        let w = World(host: false)
        w.send("n1", WidgetProposalTests.paletteArgs())
        #expect(try w.reason() == "not_ready")
        #expect(try w.status() == "rejected")
        #expect(w.handoff.pending == nil)

        let r = World()
        ResetGate.$testOverride.withValue(true) { r.send("n2", WidgetProposalTests.paletteArgs()) }
        #expect(r.handoff.pending == nil)
        #expect(r.answers.first?.1.reason == "not_ready")
    }

    @Test func whileTheDeskIsBusyTheAnswerIsBusyAndTheHeldOneStays() throws {
        let w = World()
        w.send("b0", WidgetProposalTests.paletteArgs())
        w.host.deskBusy = true
        w.send("b1", WidgetProposalTests.spriteArgs())
        #expect(try w.reason() == "busy")
        #expect(try TJ.get(w.result(), ["id"])?.string == "b1")
        #expect(w.handoff.pending?.id == "b0", "a busy answer replaces nothing")
        #expect(w.host.calls.isEmpty)
    }

    // MARK: - The words

    @Test func theReviewSaysWhatTheWidgetIsAndOffersTheCompanionForASprite() throws {
        let sprite = WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: Self.now).pack!
        let m = WidgetReview.model(id: "m1", pack: sprite, note: "A tiny friend.", deskOn: true)
        #expect(m.heading == "Claude suggests a widget · Neon blob")
        #expect(m.changesTitle == "What this widget is")
        #expect(m.option == "Make it the companion")
        #expect(m.note == "A tiny friend.")
        #expect(!m.deskOff)
        #expect(m.changes.contains { $0.hasPrefix("A Desk companion: 1 stage") })
        #expect(m.changes.contains { $0.contains("4 frames of 8x8 pixels, 3 colors") })
        #expect(m.changes.contains { $0.hasPrefix("Files: LICENSE, pack.json, sheet.json, sprite.json") })
        #expect(m.changes.contains(WidgetReviewCopy.localOnly))
        #expect(WidgetReviewCopy.localOnly == "Installs on Apply as a widget made here, on this Mac only. Nothing is sent anywhere.")

        let palette = WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!
        let pm = WidgetReview.model(id: "m2", pack: palette, note: nil, deskOn: false)
        #expect(pm.option == nil)
        #expect(pm.deskOff)
        #expect(pm.note == "Two evening gradients.")
        #expect(pm.changes.contains { $0.hasPrefix("dusk: 3 colors") })
        #expect(WidgetReview.previewInk(palette) == "ff2a6d,d16ba5,05d9e8")
        #expect(WidgetReview.previewInk(sprite) == nil)

        let effects = WidgetProposal.build(args: WidgetProposalTests.effectsArgs(), now: Self.now).pack!
        #expect(WidgetReview.describe(effects).contains("breathe: pulse, 35% intensity, every 4 s, in the ink"))
        #expect(WidgetReview.model(id: "m3", pack: effects, note: "  ", deskOn: true).canPreview == false)
        #expect(WidgetReviewCopy.heading("X") == "Claude suggests a widget · X")
        #expect(WidgetReviewCopy.barName == "Review Claude's widget")
    }

    // MARK: - The Desk's side, without a window

    @Test func theDeskModelPlacesAPreviewedSpriteOnlyWhileItIsUp() throws {
        let pack = WidgetProposal.build(args: WidgetProposalTests.spriteArgs(), now: Self.now).pack!
        let preview = try #require(CompanionPreview.make(pack))
        let model = DeskModel()
        #expect(!model.widgetPreviewsCompanion)
        model.setWidgetPreview(companion: preview)
        #expect(model.widgetPreviewsCompanion)
        model.setWidgetPreview(companion: nil)
        #expect(!model.widgetPreviewsCompanion)
        #expect(CompanionPreview.make(WidgetProposal.build(args: WidgetProposalTests.paletteArgs(), now: Self.now).pack!) == nil)
    }

    @Test func thePreviewInkIsTheSavedOneWhenNothingIsPreviewed() {
        let ink = DeskInkPreview()
        #expect(ink.resolved(saved: "ffffff") == "ffffff")
        ink.spec = "ff2a6d,05d9e8"
        #expect(ink.resolved(saved: "ffffff") == "ff2a6d,05d9e8")
        ink.spec = nil
        #expect(ink.resolved(saved: "123456") == "123456")
    }
}
