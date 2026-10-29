import Foundation
import Testing
@testable import Sanduhr

/// Claude's Desk designs, the app's review (spec 8): a request is checked again, held in memory and answered
/// `previewing`; Preview on the Desk draws it from memory (layout, font, ink, plate, shadow) without touching a saved
/// setting; Apply writes the saved Desk settings; Dismiss writes nothing. Ported from Windows' DeskDesignServiceTests,
/// DeskDesignReviewTests and DeskDirectSwitchTests. Scratch folders, an isolated defaults suite, an injected clock and a
/// fake Desk only: never the real Sanduhr folders or a Desk window.
@MainActor
@Suite("Desk design review")
struct DeskDesignReviewTests {
    nonisolated static let now = DeskDesignTests.now
    static let installed = ["EsteFont Pro", "EsteFont 26", "Helvetica Neue", "Menlo", "Georgia"]

    @MainActor
    final class FakeHost: DeskDesignPreviewHost {
        var deskRunning = true
        var deskBusy = false
        var calls: [String] = []
        var shown: DeskDesignPreviewState?
        func show(_ preview: DeskDesignPreviewState) { calls.append("show"); shown = preview }
        func end() { calls.append("end"); shown = nil }
    }

    /// One scratch world: a support folder, a defaults suite holding a standard Desk, and the handoff over them.
    @MainActor
    final class World {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-deskdesign-" + UUID().uuidString.prefix(10), isDirectory: true)
        let defaults: UserDefaults
        let suite = "sanduhr-deskdesign-" + UUID().uuidString.prefix(8)
        let host = FakeHost()
        let handoff: DeskDesignProposalHandoff
        var families = DeskDesignReviewTests.installed
        var answers: [(String, DeskAnswer)] = []
        var decided: [String] = []
        var suggested: [DeskDesignReviewModel] = []

        init(host wired: Bool = true, now: Date = DeskDesignReviewTests.now) {
            defaults = UserDefaults(suiteName: suite)!
            defaults.set("message:tl clock:bl claude:bl meetings:bl", forKey: "layout")
            defaults.set(52.0, forKey: "left")
            defaults.set(112.0, forKey: "timeSize")
            let support = root.appendingPathComponent("support", isDirectory: true)
            var fonts: () -> [String] = { [] }
            handoff = DeskDesignProposalHandoff(paths: .init(support: support), defaults: defaults, now: { now },
                                                installedFamilies: { fonts() })
            fonts = { [unowned self] in families }
            if wired { handoff.host = host }
            handoff.onAnswered = { [unowned self] in answers.append(($0, $1)) }
            handoff.onDecided = { [unowned self] in decided.append($0) }
            handoff.onSuggestion = { [unowned self] in suggested.append($0) }
        }

        deinit {
            defaults.removePersistentDomain(forName: suite)
            try? FileManager.default.removeItem(at: root)
        }

        var requestFile: URL { handoff.paths.request }
        var resultFile: URL { handoff.paths.result }

        static func request(_ id: String, _ design: String, at: Date = DeskDesignReviewTests.now) -> Data {
            Data(#"{"schema_version":1,"id":"\#(id)","requested_at":"\#(ISO8601.stamp(at))","design":\#(design)}"#.utf8)
        }

        func send(_ id: String, _ design: String, at: Date = DeskDesignReviewTests.now) {
            handoff.receive(Self.request(id, design, at: at))
        }

        func result() throws -> PackValue { TJ.text(try Data(contentsOf: resultFile)) }
        func status() throws -> String? { TJ.get(try result(), ["result", "status"])?.string }
        func reason() throws -> String? { TJ.get(try result(), ["result", "reason"])?.string }

        /// Every key and value the suite holds, to prove a preview wrote nothing.
        func snapshot() -> NSDictionary { defaults.persistentDomain(forName: suite).map { NSDictionary(dictionary: $0) } ?? [:] }
        var layout: String { defaults.string(forKey: "layout") ?? "" }
    }

    static let dusk = """
        {"name":"Dusk","note":"Warm ink for the sunset wallpaper.","plate":true,"shadow":false,
         "ink":["#ff7e5f","#ffd08a"],"font":"Menlo",
         "layout":{"clock":{"anchor":"tr","size":1.4,"tilt":-8},"claude":{"style":"ring"},"meters":{"hidden":false}}}
        """

    // MARK: - Held, answered, nothing written or drawn

    @Test func aGoodDesignIsHeldAndAnsweredPreviewingWithNothingWrittenOrDrawn() throws {
        let w = World()
        w.defaults.set("ffffff", forKey: "inkColor")
        let before = w.snapshot()
        w.send("d1", Self.dusk)
        #expect(try w.status() == "previewing")
        #expect(try TJ.get(w.result(), ["id"])?.string == "d1")
        let lines = try #require(TJ.get(try w.result(), ["result", "changes"])?.array).compactMap(\.string)
        #expect(lines.contains("Plate behind the text: on"))
        #expect(lines.contains("Font: EsteFont Pro to Menlo"))
        #expect(w.host.calls.isEmpty, "nothing is drawn until the user asks")
        #expect(w.handoff.pending?.id == "d1" && !w.handoff.previewing)
        #expect(w.suggested.map(\.id) == ["d1"])
        #expect(w.snapshot().isEqual(before))
    }

    @Test func theModelSaysWhatChangesInTheDesksOwnWords() throws {
        let w = World()
        w.defaults.set("ffffff", forKey: "inkColor")
        w.send("m1", Self.dusk)
        let m = try #require(w.handoff.model)
        #expect(m.heading == "Claude suggests a Desk design")
        #expect(m.name == "Dusk" && m.note == "Warm ink for the sunset wallpaper.")
        #expect(m.changesTitle == "What changed")
        #expect(m.changes == [
            "Clock and date: moved from bottom left to top right",
            "Clock and date: size 100% to 140%",
            "Clock and date: slant 0\u{00B0} to -8\u{00B0}",
            "Claude meters (line): ring",
            "Claude meters (bars): shown at bottom left",
            "Font: EsteFont Pro to Menlo",
            "Ink: #ffffff to #ff7e5f, #ffd08a",
            "Plate behind the text: on",
            "Drop shadow under the text: off",
        ])
        #expect(m.canApply && m.missingFonts.isEmpty && !m.previewing && !m.deskOff)
    }

    @Test func aDesignWithNoNameOrChangeSaysSo() throws {
        let w = World()
        w.send("e1", "{}")
        let m = try #require(w.handoff.model)
        #expect(m.name == "A new look for your Desk" && m.note == nil)
        #expect(m.changes == ["This design leaves your Desk as it is."])
        #expect(try TJ.get(w.result(), ["result", "changes"]) == nil)
    }

    @Test func theRequestFileIsTakenAndTheResultIsOwnerOnly() throws {
        let w = World()
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        try World.request("f1", #"{"plate":true}"#).write(to: w.requestFile)
        w.handoff.check()
        #expect(!FileManager.default.fileExists(atPath: w.requestFile.path))
        #expect(try w.status() == "previewing")
        let mode = try FileManager.default.attributesOfItem(atPath: w.resultFile.path)[.posixPermissions] as? Int
        #expect(mode == 0o600)
        #expect(w.handoff.pending?.id == "f1")
    }

    @Test func theHandoffWatchesTheSupportFolderAndTakesARequestAlreadyWaiting() throws {
        let w = World()
        let watch = HandoffWatch(dir: w.handoff.paths.support)
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        try World.request("f2", #"{"plate":true}"#).write(to: w.requestFile)
        w.handoff.start(watch: watch)
        #expect(w.handoff.pending?.id == "f2")
        watch.stop()
    }

    @Test func anExpiredRequestIsDroppedUnansweredAndTheFileGoes() throws {
        let w = World(now: Self.now.addingTimeInterval(11 * 60))
        try FileManager.default.createDirectory(at: w.handoff.paths.support, withIntermediateDirectories: true)
        try World.request("old", #"{"plate":true}"#, at: Self.now).write(to: w.requestFile)
        w.handoff.check()
        #expect(!FileManager.default.fileExists(atPath: w.requestFile.path))
        #expect(!FileManager.default.fileExists(atPath: w.resultFile.path))
        #expect(w.handoff.pending == nil && w.answers.isEmpty && w.suggested.isEmpty)
        let fresh = World(now: Self.now.addingTimeInterval(599))
        fresh.send("new", #"{"plate":true}"#)
        #expect(fresh.handoff.pending?.id == "new")
    }

    @Test func aDesignThatBreaksARuleIsRefusedWithItsReasonsAndNeverHeld() throws {
        let w = World()
        w.send("b1", #"{"launcher":{"buttons":[]},"layout":{"clock":{"monitor":"main"}}}"#)
        #expect(try w.status() == "rejected")
        #expect(try w.reason() == nil)
        let fields = try #require(TJ.get(try w.result(), ["result", "reasons"])?.array).compactMap { TJ.get($0, ["field"])?.string }
        #expect(fields.contains("launcher") && fields.contains("layout.clock"))
        #expect(w.handoff.pending == nil && w.suggested.isEmpty && w.host.calls.isEmpty)
    }

    // MARK: - Preview on the Desk

    @Test func previewingDrawsEveryFieldFromMemoryAndWritesNothing() throws {
        let w = World()
        w.defaults.set("ffffff", forKey: "inkColor")
        w.send("p1", Self.dusk)
        let before = w.snapshot()
        #expect(w.handoff.previewOnDesk())
        #expect(w.host.calls == ["show"] && w.handoff.previewing)
        let shown = try #require(w.host.shown)
        let arr = try #require(shown.layout)
        #expect(arr.placement("clock")?.anchor == .tr && arr.placement("clock")?.scale == 1.4)
        #expect(arr.tilt(of: "clock") == -8)
        #expect(arr.style(of: "claude") == .ring)
        #expect(arr.placement("meters")?.anchor == .bl)
        #expect(arr.placement("message")?.anchor == .tl, "a piece the design leaves alone stays")
        #expect(shown.font == "Menlo" && shown.ink == "ff7e5f,ffd08a" && shown.plate == true && shown.shadow == false)
        #expect(w.handoff.model?.previewing == true)
        #expect(w.snapshot().isEqual(before), "the preview wrote no saved setting")
        #expect(w.layout == "message:tl clock:bl claude:bl meetings:bl")
    }

    @Test func aPreviewDrawsOnlyTheFieldsTheDesignSets() throws {
        let w = World()
        w.send("p2", #"{"plate":true}"#)
        w.handoff.previewOnDesk()
        #expect(w.host.shown == DeskDesignPreviewState(layout: nil, font: nil, ink: nil, plate: true, shadow: nil))
    }

    @Test func theDeskHostAppliesThePreviewToTheDesksReadersAndUndoesItAllOnEnd() throws {
        let preview = DeskDesignPreview(), ink = DeskInkPreview()
        let host = DeskDesignDeskHost(preview: preview, ink: ink)
        var arr = DeskArrangement("clock:bl")
        arr.place("clock", at: .tr)
        host.show(DeskDesignPreviewState(layout: arr, font: "Menlo", ink: "ff7e5f,ffd08a", plate: true, shadow: false))
        #expect(preview.isShowing)
        #expect(preview.layout?.string == "clock:tr")
        #expect(preview.layoutString(saved: "clock:bl") == "clock:tr")
        #expect(preview.font == "Menlo" && preview.plate == true && preview.shadow == false)
        #expect(ink.resolved(saved: "ffffff") == "ff7e5f,ffd08a")
        host.end()
        #expect(!preview.isShowing)
        #expect(preview.layoutString(saved: "clock:bl") == "clock:bl")
        #expect(ink.resolved(saved: "ffffff") == "ffffff")
    }

    @Test func endingThePreviewGivesTheDeskBackAndKeepsTheDesign() throws {
        let w = World()
        w.send("e2", Self.dusk)
        w.handoff.previewOnDesk()
        w.handoff.endPreview()
        #expect(w.host.calls == ["show", "end"] && !w.handoff.previewing)
        #expect(w.handoff.pending?.id == "e2")
        #expect(w.answers.count == 1, "ending a preview answers nothing")
    }

    @Test func withTheDeskOffThereIsNothingToShowAndTheBannerSaysSo() throws {
        let w = World()
        w.host.deskRunning = false
        w.send("o1", Self.dusk)
        #expect(w.handoff.model?.deskOff == true)
        #expect(w.handoff.model?.previewHint == "Turn the Desk on to preview it.")
        #expect(!w.handoff.previewOnDesk())
        #expect(w.handoff.previewNote == "Turn the Desk on to preview it.")
        #expect(w.host.calls.isEmpty && !w.handoff.previewing)
    }

    // MARK: - Apply, dismiss

    @Test func applyWritesTheSavedDeskSettingsEndsThePreviewAndAnswersApplied() throws {
        let w = World()
        w.defaults.set("ffffff", forKey: "inkColor")
        w.send("a1", Self.dusk)
        w.handoff.previewOnDesk()
        w.handoff.apply()
        #expect(try w.status() == "applied")
        #expect(try TJ.get(w.result(), ["id"])?.string == "a1")
        #expect(w.handoff.pending == nil && !w.handoff.previewing)
        #expect(w.host.calls == ["show", "end"])
        let saved = DeskArrangement(w.layout)
        #expect(saved.placement("clock")?.anchor == .tr && saved.placement("clock")?.scale == 1.4)
        #expect(saved.tilt(of: "clock") == -8 && saved.style(of: "claude") == .ring)
        #expect(saved.placement("meters")?.anchor == .bl && saved.placement("message")?.anchor == .tl)
        #expect(w.defaults.string(forKey: "font") == "Menlo")
        #expect(w.defaults.string(forKey: "inkColor") == "ff7e5f,ffd08a")
        #expect(w.defaults.bool(forKey: "plate") == true)
        #expect(w.defaults.object(forKey: "inkShadow") as? Bool == false)
        #expect(w.defaults.double(forKey: "left") == 52 && w.defaults.double(forKey: "timeSize") == 112, "margins and sizes are not a design's")
        let lines = try #require(TJ.get(try w.result(), ["result", "changes"])?.array)
        #expect(lines.count == 9)
        #expect(w.decided == ["a1"])
    }

    @Test func applyWithoutAPreviewStillWritesAndAPartialDesignLeavesTheRestAlone() throws {
        let w = World()
        w.defaults.set("Georgia", forKey: "font")
        w.defaults.set("123456", forKey: "inkColor")
        w.send("a2", #"{"plate":true}"#)
        w.handoff.apply()
        #expect(try w.status() == "applied")
        #expect(w.defaults.bool(forKey: "plate") == true)
        #expect(w.defaults.string(forKey: "font") == "Georgia" && w.defaults.string(forKey: "inkColor") == "123456")
        #expect(w.layout == "message:tl clock:bl claude:bl meetings:bl")
        #expect(w.defaults.object(forKey: "inkShadow") == nil)
    }

    @Test func aHiddenPieceGoesAndAMovedPieceKeepsItsSize() throws {
        let w = World()
        w.defaults.set("message:tl clock:bl:1.2 claude:bl meetings:bl", forKey: "layout")
        w.send("a3", #"{"layout":{"meetings":{"hidden":true},"clock":{"anchor":"br"}}}"#)
        w.handoff.apply()
        let saved = DeskArrangement(w.layout)
        #expect(saved.placement("meetings") == nil)
        #expect(saved.placement("clock")?.anchor == .br && saved.placement("clock")?.scale == 1.2)
    }

    @Test func dismissWritesNothingTakesThePreviewDownAndAnswersDismissed() throws {
        let w = World()
        w.defaults.set("ffffff", forKey: "inkColor")
        w.send("x1", Self.dusk)
        w.handoff.previewOnDesk()
        let before = w.snapshot()
        w.handoff.dismiss()
        #expect(try w.status() == "rejected" && w.reason() == "dismissed")
        #expect(w.handoff.pending == nil && !w.handoff.previewing)
        #expect(w.host.calls == ["show", "end"] && w.decided == ["x1"])
        #expect(w.snapshot().isEqual(before))
    }

    // MARK: - Replaced, busy, not ready

    @Test func aNewerDesignAnswersTheOlderReplacedAndTakesItsPreviewDown() throws {
        let w = World()
        w.send("r1", Self.dusk)
        w.handoff.previewOnDesk()
        w.send("r2", #"{"plate":true}"#)
        let old = try #require(w.answers.first { $0.0 == "r1" && $0.1.reason == "replaced" })
        #expect(old.1.status == "rejected")
        #expect(w.handoff.pending?.id == "r2" && !w.handoff.previewing)
        #expect(w.host.calls == ["show", "end"] && w.decided == ["r1"])
        #expect(try w.status() == "previewing" && TJ.get(w.result(), ["id"])?.string == "r2")
    }

    @Test func whileArrangeDeskIsActiveTheAnswerIsBusyAndTheHeldOneStays() throws {
        let w = World()
        w.send("b0", #"{"plate":true}"#)
        w.host.deskBusy = true
        w.send("b1", Self.dusk)
        #expect(try w.reason() == "busy" && w.status() == "rejected")
        #expect(try TJ.get(w.result(), ["id"])?.string == "b1")
        #expect(w.handoff.pending?.id == "b0", "a busy answer replaces nothing")
        #expect(w.host.calls.isEmpty)
    }

    @Test func withNoDeskWiredTheAnswerIsNotReadyAndAfterAResetToo() throws {
        let w = World(host: false)
        w.send("n1", #"{"plate":true}"#)
        #expect(try w.reason() == "not_ready" && w.status() == "rejected")
        #expect(w.handoff.pending == nil)
        let r = World()
        ResetGate.$testOverride.withValue(true) { r.send("n2", #"{"plate":true}"#) }
        #expect(r.handoff.pending == nil)
        #expect(r.answers.first?.1.reason == "not_ready")
    }

    // MARK: - A font this Mac lacks

    @Test func aMissingFontPreviewsInTheNearestFontAndApplyWaitsUntilItIsInstalled() throws {
        let w = World()
        w.send("f1", #"{"name":"Zed","font":{"family":"Zed Mono","link":"https://fonts.example/zed"},"plate":true}"#)
        #expect(try w.status() == "previewing")
        #expect(try TJ.get(w.result(), ["result", "fonts"])?.array?.first.flatMap { TJ.get($0, ["family"])?.string } == "Zed Mono")
        let m = try #require(w.handoff.model)
        #expect(!m.canApply)
        #expect(m.missingFonts == [DeskReviewFont(family: "Zed Mono", link: "https://fonts.example/zed")])
        #expect(m.missingFonts[0].line == "Zed Mono is not installed. Get it from https://fonts.example/zed, then install it.")
        w.handoff.previewOnDesk()
        #expect(w.host.shown?.font == "Menlo", "the nearest installed look")

        w.handoff.apply()   // Apply waits: pressing it does nothing
        #expect(w.handoff.pending != nil && w.defaults.object(forKey: "plate") == nil && w.answers.count == 1)

        w.families.append("Zed Mono")   // the font arrives
        w.handoff.fontsChanged()
        #expect(w.host.shown?.font == "Zed Mono")
        #expect(w.handoff.model?.canApply == true)
        w.handoff.apply()
        #expect(try w.status() == "applied")
        #expect(w.defaults.string(forKey: "font") == "Zed Mono" && w.defaults.bool(forKey: "plate"))
    }

    @Test func aFontWithNoLinkSaysToInstallItAndAnInstalledOrBundledOneNeedsNothing() throws {
        let w = World()
        w.send("f2", #"{"font":"Zed Mono"}"#)
        #expect(w.handoff.model?.missingFonts.first?.line == "Zed Mono is not installed. Install it to apply this design.")
        let ok = World()
        ok.send("f3", #"{"font":"EsteFont 26"}"#)
        #expect(ok.handoff.model?.canApply == true)
        #expect(try TJ.get(ok.result(), ["result", "fonts"]) == nil)
    }

    @Test func thePreviewFontIsTheNearestLookThisMacHas() {
        let have: (String) -> Bool = { ["Menlo", "Georgia", "Helvetica Neue"].contains($0) }
        #expect(DeskDesignApply.previewFont("Zed Mono", installed: have) == "Menlo")
        #expect(DeskDesignApply.previewFont("Playfair Display", installed: have) == "Georgia")
        #expect(DeskDesignApply.previewFont("Some Sans", installed: have) == "Helvetica Neue")
        #expect(DeskDesignApply.previewFont("Brush Script", installed: have) == "EsteFont Pro")
        #expect(DeskDesignApply.previewFont("Menlo", installed: have) == "Menlo")
        #expect(DeskDesignApply.previewFont("", installed: have) == "")
        #expect(DeskDesignApply.previewFont("EsteFont 26", installed: { _ in false }) == "EsteFont 26")
    }

    // MARK: - The direct switch never covers a whole design

    @Test func noSettingLetsClaudeSkipApplyForAWholeDesign() throws {
        let keys = [DeskMessageHandoff.directKey, ThemeProposalHandoff.directKey, NowPlayingLooks.directKey]
        let std = UserDefaults.standard
        let saved = keys.map { std.object(forKey: $0) }
        keys.forEach { std.set(true, forKey: $0) }
        defer { for (k, v) in zip(keys, saved) { if let v { std.set(v, forKey: k) } else { std.removeObject(forKey: k) } } }
        let w = World()
        keys.forEach { w.defaults.set(true, forKey: $0) }
        let before = w.snapshot()
        w.send("d9", Self.dusk)
        #expect(try w.status() == "previewing")
        #expect(w.handoff.pending?.id == "d9")
        #expect(w.snapshot().isEqual(before), "a design waits for Apply whatever the switches say")
        #expect(w.layout == "message:tl clock:bl claude:bl meetings:bl")
    }

    // MARK: - The pure pieces

    @Test func theMergeShowsAHiddenPieceAtItsStandardAnchorAndSlantsTheWholeStack() {
        let current = DeskDesignLook(arrangement: DeskArrangement("message:tl clock:bl"), font: nil, ink: "ffffff", plate: false, shadow: true)
        let design = DeskDesign(layout: [DeskPieceLayout(piece: "claude", hidden: false), DeskPieceLayout(piece: "clock", tilt: 6)])
        let next = DeskDesignApply.merged(current, design)
        #expect(next.arrangement.placement("claude")?.anchor == .bl && next.arrangement.placement("claude")?.scale == 1)
        #expect(next.arrangement.tilt(of: "claude") == 6 && next.arrangement.tilt(of: "clock") == 6, "one slant per stack")
        #expect(next.ink == "ffffff" && next.font == nil && !next.plate && next.shadow)
    }

    @Test func aStyleOrSizeOnAHiddenPieceChangesNothingAndWriteTouchesOnlyWhatDiffers() {
        let store = UserDefaults(suiteName: "sanduhr-apply-" + UUID().uuidString.prefix(8))!
        let current = DeskDesignLook.read(store)
        #expect(current.arrangement.string == DeskLayout.standard && current.ink == "ffffff" && current.shadow && !current.plate)
        let next = DeskDesignApply.merged(current, DeskDesign(layout: [DeskPieceLayout(piece: "watchers", size: 1.5)]))
        #expect(next == current)
        DeskDesignApply.write(next, over: current, to: store)
        #expect(store.object(forKey: "layout") == nil && store.object(forKey: "plate") == nil)
    }

    // MARK: - Put my Desk back

    @Test func applyThenPutBackRestoresEveryChangedKeyAndAbsentStaysAbsent() throws {
        let w = World()
        w.defaults.set("ffffff", forKey: "inkColor")   // font, plate and inkShadow are absent
        w.send("u1", Self.dusk)
        #expect(w.defaults.string(forKey: "deskDesignBefore") == nil, "a preview records nothing")
        w.handoff.apply()
        let undo = try #require(DeskDesignApply.Undo.read(w.defaults))
        #expect(undo.name == "Dusk" && undo.at == Self.now)
        #expect(w.defaults.string(forKey: "font") == "Menlo")
        undo.putBack(in: w.defaults)
        #expect(w.layout == "message:tl clock:bl claude:bl meetings:bl")
        #expect(w.defaults.string(forKey: "inkColor") == "ffffff")
        #expect(w.defaults.object(forKey: "font") == nil && w.defaults.object(forKey: "plate") == nil && w.defaults.object(forKey: "inkShadow") == nil)
        #expect(w.defaults.object(forKey: "deskDesignBefore") == nil)
        #expect(w.defaults.double(forKey: "left") == 52)
    }

    @Test func onlyTheKeysTheDesignChangesAreSaved() throws {
        let w = World()
        w.defaults.set("Georgia", forKey: "font")
        w.send("u2", #"{"name":"Plain","plate":true}"#)
        w.handoff.apply()
        w.defaults.set("Helvetica Neue", forKey: "font")   // changed by hand afterwards
        try #require(DeskDesignApply.Undo.read(w.defaults)).putBack(in: w.defaults)
        #expect(w.defaults.object(forKey: "plate") == nil)
        #expect(w.defaults.string(forKey: "font") == "Helvetica Neue", "a key the design did not change is not touched")
    }

    @Test func keepItClearsOnlyTheSnapshot() throws {
        let w = World()
        w.send("u3", Self.dusk)
        w.handoff.apply()
        let after = w.snapshot().mutableCopy() as! NSMutableDictionary
        after.removeObject(forKey: "deskDesignBefore")
        DeskDesignApply.keepIt(in: w.defaults)
        #expect(w.snapshot().isEqual(after))
        #expect(DeskDesignApply.Undo.read(w.defaults) == nil)
    }

    @Test func aSecondApplyReplacesTheSnapshot() throws {
        let w = World()
        w.send("u4", #"{"name":"One","plate":true}"#)
        w.handoff.apply()
        w.send("u5", #"{"name":"Two","shadow":false}"#)
        w.handoff.apply()
        let undo = try #require(DeskDesignApply.Undo.read(w.defaults))
        #expect(undo.name == "Two")
        undo.putBack(in: w.defaults)
        #expect(w.defaults.bool(forKey: "plate") == true, "the first design stays; only the second is undone")
        #expect(w.defaults.object(forKey: "inkShadow") == nil)
    }

    @Test func theCaptionNamesTheDesignAndWhenItWasApplied() throws {
        let w = World()
        w.send("u6", Self.dusk)
        w.handoff.apply()
        let undo = try #require(DeskDesignApply.Undo.read(w.defaults))
        #expect(undo.caption(now: Self.now.addingTimeInterval(120)) == "Undo \"Dusk\" from Claude, applied 2 minutes ago.")
    }

    @Test func resetRemovesTheSnapshotWithTheDeskDomain() {
        // Reset clears the whole com.626labs.sanduhr.desk domain (Reset.swift), and the snapshot is a key in it.
        #expect(Reset.deskDomain == "com.626labs.sanduhr.desk")
    }

    @Test func theDebugSnapshotFlagsAWaitingDesignAndNothingElse() {
        let flags = DebugState.pendingSuggestionsYAML(messages: false, theme: false, widget: false, desk: true)
        let text = YAMLEmitter.emit(flags)
        #expect(text.contains("desk: true"))
        #expect(!text.contains("Dusk"))
    }
}
