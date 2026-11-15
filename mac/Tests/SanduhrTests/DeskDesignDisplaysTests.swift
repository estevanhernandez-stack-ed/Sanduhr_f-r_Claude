import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// The agent-designed Desk per display (spec, Desk pieces per display): get_desk's `monitors` and each piece's
/// `monitor`, propose_desk's `layout.<piece>.monitor`, and what Apply writes and says. Made-up display ids only.
@Suite("Desk design per display")
struct DeskDesignDisplaysTests {
    static let mainID = "3C1A9E52-7B44-4F0D-A6E2-11D8B0C4F9A7"
    static let sideID = "9B2E6D10-55AF-4C3B-8E71-D04A2F6C8B35"
    /// The main display, and a 1024×768 one to its left, tops aligned.
    static let screens = DeskScreens.named([
        DeskScreenInfo(id: mainID, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), primary: true),
        DeskScreenInfo(id: sideID, frame: CGRect(x: -1024, y: 214, width: 1024, height: 768), primary: false),
    ])

    private func suite() -> UserDefaults {
        let name = "sanduhr.tests.deskdisplays.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: name)!
        d.removePersistentDomain(forName: name)
        return d
    }

    private func state(_ d: UserDefaults, screens: [DeskScreenInfo]) throws -> [String: Any] {
        let data = DeskStateExport.stateJson(defaults: d, installedFonts: [], writerVersion: "2.44.0", screens: screens,
                                             fontAvailable: { _ in true })
        return try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private func validate(_ json: String, displays: [String: DeskDisplay]? = nil) -> DeskValidation {
        DeskDesign.validate(TJ.parse(json), context: DeskDesignContext(installedFonts: [], monitors: Self.screens, displays: displays))
    }

    // MARK: - get_desk

    @Test func twoScreensExportAsMonitorsAndEveryPlacedPieceSaysWhereItIs() throws {
        let d = suite()
        d.set("message:tl@all clock:bl@\(Self.sideID) claude:bl", forKey: "layout")
        let s = try state(d, screens: Self.screens)
        let monitors = try #require(s["monitors"] as? [[String: Any]])
        #expect(monitors.count == 2)
        #expect(monitors[0]["id"] as? String == Self.mainID)
        #expect(monitors[0]["name"] as? String == "1512×982, main")
        #expect(monitors[0]["primary"] as? Bool == true)
        #expect(monitors[0]["left"] as? Int == 0 && monitors[0]["top"] as? Int == 0)
        #expect(monitors[1]["name"] as? String == "1024×768, left of main")
        #expect(monitors[1]["width"] as? Int == 1024 && monitors[1]["height"] as? Int == 768)
        #expect(monitors[1]["primary"] as? Bool == false)
        #expect(monitors[1]["left"] as? Int == -1024 && monitors[1]["top"] as? Int == 0)
        let layout = try #require((s["design"] as? [String: Any])?["layout"] as? [String: [String: Any]])
        #expect(layout["message"]?["monitor"] as? String == "all")
        #expect(layout["clock"]?["monitor"] as? String == Self.sideID)
        #expect(layout["claude"]?["monitor"] as? String == "main")
        #expect(layout["meters"]?["monitor"] == nil)
    }

    @Test func oneScreenStillExportsOneMonitor() throws {
        let one = [DeskScreenInfo(id: Self.mainID, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), primary: true)]
        let monitors = try #require(try state(suite(), screens: one)["monitors"] as? [[String: Any]])
        #expect(monitors.count == 1)
        #expect(monitors[0]["name"] as? String == "1512×982, main")
        #expect(monitors[0]["primary"] as? Bool == true)
    }

    // MARK: - propose_desk

    @Test func anUnknownIdIsRefusedNamingTheField() {
        let v = validate(#"{"layout":{"clock":{"anchor":"tl","monitor":"00000000-1111-2222-3333-444444444444"}}}"#)
        #expect(v.status == .rejected)
        #expect(v.reasons == [DeskReason("layout.clock.monitor",
                                         "is not a display connected to this Mac; use \"all\", \"main\" or an id from monitors in get_desk.")])
    }

    @Test func aKnownIdPassesInAnyCaseAndAWordThatIsNotAStringIsRefused() throws {
        let ok = validate(#"{"layout":{"clock":{"monitor":"\#(Self.sideID.lowercased())"}}}"#)
        #expect(ok.ok)
        #expect(try #require(ok.design).layout?.first?.monitor == .id(Self.sideID.lowercased()))
        let bad = validate(#"{"layout":{"clock":{"monitor":true}}}"#)
        #expect(bad.reasons.map(\.field) == ["layout.clock.monitor"])
    }

    @Test func slantsArePerDisplayAndAPieceWithNoMonitorKeepsItsDisplay() {
        // Same anchor, two displays: two stacks.
        #expect(validate(#"{"layout":{"clock":{"anchor":"tl","tilt":8,"monitor":"\#(Self.sideID)"},"claude":{"anchor":"tl","tilt":-8}}}"#).ok)
        // The main display's own id is the main display's stack.
        #expect(!validate(#"{"layout":{"clock":{"anchor":"tl","tilt":8,"monitor":"\#(Self.mainID)"},"claude":{"anchor":"tl","tilt":-8}}}"#).ok)
        // claude names no monitor and sits on the side display now, so it shares that stack with clock.
        let sharing = validate(#"{"layout":{"clock":{"anchor":"tl","tilt":8,"monitor":"\#(Self.sideID)"},"claude":{"anchor":"tl","tilt":-8}}}"#,
                               displays: ["claude": .id(Self.sideID)])
        #expect(sharing.reasons.map(\.field) == ["layout.clock.tilt"])
    }

    // MARK: - Apply

    @Test func applyWritesTheLayoutStringAndTheLinesNameTheDisplay() throws {
        let current = DeskDesignLook(arrangement: DeskArrangement("clock:tl"), font: nil, ink: "ffffff", plate: false, shadow: true)
        let design = try #require(validate(#"{"layout":{"clock":{"monitor":"\#(Self.sideID.lowercased())"}}}"#).design)
        let next = DeskDesignApply.merged(current, design, screens: Self.screens)
        #expect(next.arrangement.string == "clock:tl:1:0:bar@\(Self.sideID)")
        #expect(next.arrangement.placement("clock")?.monitor == .id(Self.sideID))
        #expect(DeskDesignApply.changes(from: current, to: next, screens: Self.screens) == ["Clock and date: moved to 1024×768, left of main"])

        let all = DeskDesignApply.merged(current, try #require(validate(#"{"layout":{"clock":{"monitor":"all"}}}"#).design), screens: Self.screens)
        #expect(all.arrangement.placement("clock")?.monitor == .all)
        #expect(DeskDesignApply.changes(from: current, to: all, screens: Self.screens) == ["Clock and date: now on all displays"])
    }

    @Test func theMainDisplaysOwnIdWritesMainAndChangesNothing() throws {
        let current = DeskDesignLook(arrangement: DeskArrangement("clock:tl"), font: nil, ink: "ffffff", plate: false, shadow: true)
        let design = try #require(validate(#"{"layout":{"clock":{"monitor":"\#(Self.mainID)"}}}"#).design)
        let next = DeskDesignApply.merged(current, design, screens: Self.screens)
        #expect(next.arrangement.placement("clock")?.monitor == .main)
        #expect(DeskDesignApply.changes(from: current, to: next, screens: Self.screens).isEmpty)
    }

    @Test func aPieceShownOnAnotherDisplaySaysWhere() throws {
        let current = DeskDesignLook(arrangement: DeskArrangement("clock:tl"), font: nil, ink: "ffffff", plate: false, shadow: true)
        let design = try #require(validate(#"{"layout":{"weather":{"anchor":"br","monitor":"\#(Self.sideID)"}}}"#).design)
        let next = DeskDesignApply.merged(current, design, screens: Self.screens)
        #expect(next.arrangement.placement("weather")?.monitor == .id(Self.sideID))
        #expect(DeskDesignApply.changes(from: current, to: next, screens: Self.screens)
                == ["Weather: shown at bottom right, on 1024×768, left of main"])
    }
}
