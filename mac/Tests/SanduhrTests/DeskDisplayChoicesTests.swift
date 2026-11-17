import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// 2.44 item 6: the Display picker's choices and the Order list's grouping.
private let secondID = "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6"
private let mainID = "11111111-0000-4000-8000-000000000001"

private func one() -> [DeskScreenInfo] {
    DeskScreens.named([DeskScreenInfo(id: mainID, frame: CGRect(x: 0, y: 0, width: 1920, height: 1080), primary: true)])
}

private func two() -> [DeskScreenInfo] {
    DeskScreens.named([
        DeskScreenInfo(id: mainID, frame: CGRect(x: 0, y: 0, width: 1920, height: 1080), primary: true),
        DeskScreenInfo(id: secondID, frame: CGRect(x: -1024, y: 0, width: 1024, height: 768), primary: false),
    ])
}

@Suite("Desk display picker model, 2.44")
struct DeskDisplayChoicesTests {
    @Test func oneScreenHidesThePicker() {
        let a = DeskArrangement("clock:tl meters:bl")
        #expect(!DeskDisplayChoices.shows(a, screens: one()))
        let p = a.placement("clock")!
        #expect(DeskDisplayChoices.choices(for: p, screens: one()).map(\.title) == ["Main display", "All displays"])
    }

    @Test func twoScreensListMainAllAndTheSecondByName() {
        let a = DeskArrangement("clock:tl")
        #expect(DeskDisplayChoices.shows(a, screens: two()))
        let c = DeskDisplayChoices.choices(for: a.placement("clock")!, screens: two())
        #expect(c.map(\.title) == ["Main display", "All displays", "1024×768, left of main"])
        #expect(DeskDisplayChoices.selection(for: a.placement("clock")!, screens: two()).kind == .main)
    }

    @Test func aStrandedPieceGetsTheExtraChoiceAndShowsThePicker() {
        let a = DeskArrangement("clock:tl@\(secondID)")
        #expect(DeskDisplayChoices.shows(a, screens: one()))
        let p = a.placement("clock")!
        let c = DeskDisplayChoices.choices(for: p, screens: one())
        #expect(c.last?.title == "A display that is not connected (on the main display for now)")
        #expect(DeskDisplayChoices.selection(for: p, screens: one()).kind == .notConnected)
    }

    @Test func picksWriteTheLayoutThroughSetDisplay() {
        var a = DeskArrangement("clock:tl meters:bl")
        let screens = two()
        let c = DeskDisplayChoices.choices(for: a.placement("clock")!, screens: screens)
        DeskDisplayChoices.apply(c[2], widget: "clock", to: &a, screens: screens)
        #expect(a.string.contains("clock:tl") && a.string.contains("@\(secondID)"))
        #expect(a.placement("clock")?.monitor == .id(secondID))
        DeskDisplayChoices.apply(c[1], widget: "clock", to: &a, screens: screens)
        #expect(a.placement("clock")?.monitor == .all)
        DeskDisplayChoices.apply(c[0], widget: "clock", to: &a, screens: screens)
        #expect(a.placement("clock")?.monitor == .main)
        #expect(a.string == DeskArrangement("clock:tl meters:bl").string)
    }

    @Test func theStrandedChoiceWritesNothing() {
        var a = DeskArrangement("clock:tl@\(secondID)")
        let before = a.string
        let c = DeskDisplayChoices.selection(for: a.placement("clock")!, screens: one())
        DeskDisplayChoices.apply(c, widget: "clock", to: &a, screens: one())
        #expect(a.string == before)
    }

    @Test func theMainDisplaysOwnIdReadsAsMain() {
        let a = DeskArrangement("clock:tl@\(mainID)")
        #expect(DeskDisplayChoices.selection(for: a.placement("clock")!, screens: two()).kind == .main)
        #expect(!DeskDisplayChoices.shows(a, screens: one()))
        let screens = two()
        var b = DeskArrangement("clock:tl@\(secondID)")
        let main = DeskDisplayChoices.choices(for: b.placement("clock")!, screens: screens)[0]
        DeskDisplayChoices.apply(main, widget: "clock", to: &b, screens: screens)
        #expect(b.placement("clock")?.monitor == .main)
    }

    @Test func orderGroupsAreUnchangedOnOneDisplay() {
        let a = DeskArrangement("clock:tl meters:tl message:bl")
        let g = DeskDisplayChoices.orderGroups(a, screens: one())
        #expect(g.map(\.anchor) == [.tl, .bl])
        #expect(g.allSatisfy { $0.displayTitle == nil })
        #expect(g[0].pieces.map(\.widget).sorted() == ["clock", "meters"])
    }

    @Test func orderGroupsListEveryPieceByDisplayOnTwo() {
        let a = DeskArrangement("clock:tl meters:tl@all message:bl@\(secondID) claude:br@\(UUID().uuidString)")
        let g = DeskDisplayChoices.orderGroups(a, screens: two())
        #expect(g.map(\.displayTitle) == ["Main display", "All displays", "1024×768, left of main", "A display that is not connected"])
        #expect(g.flatMap(\.pieces).count == 4)
    }

    @Test func searchFindsTheDisplayPicker() {
        for word in ["display", "displays", "monitor", "screen", "second screen"] {
            #expect(SettingsSearch.hits(word).contains { $0.entry.anchor == SettingsAnchor.pieces })
        }
    }
}
