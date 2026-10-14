import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice B: the Mod Studio's draft looks, Save and Revert, and its moments.
@Suite("Mod Studio")
@MainActor
struct ModStudioTests {
    private func gradient() -> SegmentStyle {
        var s = SegmentStyle()
        s.ink = ["#ff2a6d", "#05d9e8"]
        s.bold = true
        return s
    }

    @Test func aDraftIsNotWrittenUntilSave() {
        var written: [[SanduhrSegment: SegmentStyle]] = []
        let model = ModStudioModel(saved: [:]) { written.append($0) }
        #expect(!model.isDirty)
        model.setStyle(gradient(), for: .session)
        #expect(model.isDirty && written.isEmpty)
        model.save()
        #expect(written == [[.session: gradient()]] && !model.isDirty)
    }

    @Test func revertDropsTheDraftAndAnEmptyLookIsNoLook() {
        let model = ModStudioModel(saved: [.weekly: gradient(), .label: gradient()]) { _ in }
        // Only the band's segments are the Studio's.
        #expect(model.saved == [.weekly: gradient()])
        model.setStyle(SegmentStyle(), for: .weekly)
        #expect(model.isDirty && model.style(.weekly).isEmpty)
        model.revert()
        #expect(!model.isDirty && model.style(.weekly) == gradient())
    }

    @Test func thePreviewDrawsTheDraft() {
        let model = ModStudioModel(saved: [:]) { _ in }
        model.setStyle(gradient(), for: .session)
        let lines = model.lines(at: Date(timeIntervalSince1970: 1_790_000_000))
        let session = lines.first?.prefix(7) ?? []
        // One color per letter of "Session", from the first stop, bold.
        #expect(session.map(\.text).joined() == "Session")
        #expect(session.first?.color == "#ff2a6d" && session.allSatisfy(\.bold))
    }

    @Test func everyMomentDrawsAndOnlyWatchersHasRows() {
        let model = ModStudioModel(saved: [:]) { _ in }
        let at = Date(timeIntervalSince1970: 1_790_000_000)
        for m in StudioMoment.allCases {
            model.moment = m
            let lines = model.lines(at: at)
            #expect(lines.count == (m == .watchers ? 3 : 1), "\(m)")
        }
        model.moment = .nearlyFull
        #expect(model.lines(at: at).first?.contains { $0.text == "⚠" } == true)
        model.layout.style = .full
        #expect(model.lines(at: at).count == 2)
    }

    @Test func theCrossingSweepReplays() {
        let start = 1_000_000.0
        let m = StudioMoment.crossing
        #expect(m.sweeps(now: start + 100, startedAt: start)[BandFrame.session] == start + 300)
        #expect(m.sweeps(now: start + 3100, startedAt: start)[BandFrame.session] == start + 3300)
        #expect(StudioMoment.calm.sweeps(now: start, startedAt: start).isEmpty)
    }

    // Slice C: the folder rows moved into the Studio's Folders tab; the page keeps one line.

    private func row(_ folder: String, on: Bool, listed: Bool = true, pinned: String? = nil, waiting: Bool = false) -> OwnModRow {
        var state = OwnModState(folder: folder)
        state.entry = listed ? "/mods/sanduhr-meters" : nil
        state.pinned = pinned
        state.inline = on ? nil : false
        var r = OwnModRow(state: state)
        r.updateAvailable = waiting
        return r
    }

    @Test func theOnLineCountsFoldersAndWaitingUpdates() {
        #expect(OwnModRow.onLine([]) == nil)
        #expect(OwnModRow.onLine([row("/a", on: true)]) == "On in 1 of 1 folder")
        let rows = [row("/a", on: true, waiting: true), row("/b", on: false), row("/c", on: false, listed: false, waiting: true)]
        // An unlisted folder has nothing to update.
        #expect(OwnModRow.onLine(rows) == "On in 1 of 3 folders, an update waiting in 1")
    }

    @Test func aMissingFolderWaitsForRemoveNotUpdate() {
        var r = row("/a", on: true, waiting: true)
        #expect(r.updateWaiting)
        r.missing = true
        #expect(!r.updateWaiting)
        var outdated = row("/b", on: true)
        outdated.status = .outdated
        #expect(outdated.updateWaiting)
    }

    @Test func theStudioOpensOnTheTabAsked() {
        let model = ModsPageModel()
        model.openStudio(.folders)
        #expect(model.studioOpen && model.studioTab == .folders)
        model.studioOpen = false
        model.openStudio()
        #expect(model.studioTab == .look)
        #expect(StudioTab.allCases.map(\.title) == ["Look", "Layout", "Folders", "About"])
    }

    @Test func checkWaitsForTheFoldersToBeRead() async {
        let model = ModsPageModel()
        await model.checkOwnMod()
        // Before the read, `claude` isn't known yet: no answer rather than "unavailable".
        #expect(model.ownCheck == nil && !model.checkingOwn)
    }

    // Slice D: Layout drafts the mod's options for one folder or every folder where it's on.

    @Test func layoutDraftsForTheFolderShownOrEveryFolderWhereItsOn() {
        let model = ModStudioModel(saved: [:]) { _ in }
        var a = row("/a", on: true)
        a.options.values.style = .full
        a.options.isSet = true
        a.options.isOurs = true
        let b = row("/b", on: true)
        var c = row("/c", on: false)
        c.options.isSet = true
        let rows = [a, b, c]
        model.showLayout(of: a)
        #expect(model.layout.style == .full && !model.layoutDirty(rows))
        model.layoutEverywhere = true
        // /b still has the defaults; /c is off.
        #expect(model.layoutTargets(rows).map(\.folder) == ["/a", "/b"] && model.layoutDirty(rows))
        model.layoutEverywhere = false
        model.layout.label = "Personal"
        #expect(model.layoutDirty(rows))
        model.revert()
        #expect(model.layout.label.isEmpty && !model.layoutDirty(rows))
        // Options someone else set aren't Save's to change.
        model.showLayout(of: c)
        model.layout.bars = .weekly
        #expect(!model.layoutDirty(rows))
    }

    @Test func thePreviewDrawsTheLayout() {
        let model = ModStudioModel(saved: [:]) { _ in }
        let at = Date(timeIntervalSince1970: 1_790_000_000)
        model.layout.bars = .session
        let one = model.lines(at: at).first.map { $0.map(\.text).joined() } ?? ""
        #expect(one.contains("Session") && !one.contains("Weekly"))
        model.layout.label = " Personal "
        #expect(model.lines(at: at).first?.first?.text == "Personal")
    }
}
