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
        model.full = true
        #expect(model.lines(at: at).count == 2)
    }

    @Test func theCrossingSweepReplays() {
        let start = 1_000_000.0
        let m = StudioMoment.crossing
        #expect(m.sweeps(now: start + 100, startedAt: start)[BandFrame.session] == start + 300)
        #expect(m.sweeps(now: start + 3100, startedAt: start)[BandFrame.session] == start + 3300)
        #expect(StudioMoment.calm.sweeps(now: start, startedAt: start).isEmpty)
    }
}
