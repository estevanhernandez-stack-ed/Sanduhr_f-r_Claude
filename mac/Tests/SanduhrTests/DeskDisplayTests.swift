import Foundation
import Testing
@testable import Sanduhr

/// 2.44 item 2: a display per placed piece, in the layout string.

/// A 2.43 build's read of a word: split on space then `:`, field 1 anchor, 2 size, 3 tilt.
private func read243(_ layout: String) -> [(widget: String, anchor: String, scale: Double, tilt: Double)] {
    layout.split(separator: " ").compactMap { word in
        let bits = word.split(separator: ":").map(String.init)
        guard bits.count >= 2 else { return nil }
        return (bits[0], bits[1], bits.count >= 3 ? Double(bits[2]) ?? 1 : 1, bits.count >= 4 ? Double(bits[3]) ?? 0 : 0)
    }
}

private let uuid = "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6"

@Suite("Desk display per piece, 2.44")
struct DeskDisplayTests {
    @Test func every243StringRoundTripsUnchanged() {
        let strings = [
            DeskLayout.standard,
            "clock:tl message:tr meters:bl",
            "clock:tc:1.2 meters:br:0.8 message:ml",
            "clock:bl:1:-15 claude:bl:1.2:-15",
            "meters:tr:1:0:ring claude:bl:1.2:8:slim telemetry:tr:1:0:ticks",
        ]
        for s in strings {
            let a = DeskArrangement(s)
            #expect(a.pieces.allSatisfy { $0.monitor == .main })
            #expect(a.string == s)
        }
    }

    @Test func bothSuffixFormsParse() {
        #expect(DeskArrangement("clock:tl@all").placement("clock") == DeskPlacement(widget: "clock", anchor: .tl, monitor: .all))
        #expect(DeskArrangement("clock:tl:1.2@all").placement("clock") == DeskPlacement(widget: "clock", anchor: .tl, scale: 1.2, monitor: .all))
        #expect(DeskArrangement("clock:tl:1:0:bar@all").placement("clock")?.monitor == .all)
        #expect(DeskArrangement("claude:bl:1.2:-15:slim@\(uuid)").placement("claude")
                == DeskPlacement(widget: "claude", anchor: .bl, scale: 1.2, tilt: -15, style: .slim, monitor: .id(uuid)))
        #expect(DeskArrangement("clock:tl@\(uuid)").placement("clock")?.monitor == .id(uuid))
    }

    @Test func mainEmptyAndBadIdsReadAsMain() {
        for suffix in ["@main", "@", "@a@b", "@" + String(repeating: "x", count: 301)] {
            #expect(DeskArrangement("clock:tl" + suffix).placement("clock")?.monitor == .main, "\(suffix.prefix(12))")
        }
        #expect(DeskDisplay.parse("a b") == .main)
        #expect(DeskDisplay.parse("a:b") == .main)
        #expect(DeskDisplay.parse(String(repeating: "x", count: 300)) == .id(String(repeating: "x", count: 300)))
        #expect(DeskArrangement("clock:tl@main").string == "clock:tl")
    }

    @Test func longFormIsWrittenAndKeepsTheAnchorInA243Build() {
        var a = DeskArrangement("clock:tl:1.2:-10 claude:bl")
        a.setDisplay("clock", .all)
        a.setDisplay("claude", .id(uuid))
        #expect(a.string == "clock:tl:1.2:-10:bar@all claude:bl:1:0:bar@\(uuid)")
        let old = read243(a.string)
        #expect(old[0].widget == "clock" && old[0].anchor == "tl" && old[0].scale == 1.2 && old[0].tilt == -10)
        #expect(old[1].widget == "claude" && old[1].anchor == "bl" && old[1].scale == 1 && old[1].tilt == 0)
        #expect(DeskArrangement(a.string) == a)

        var b = DeskArrangement("claude:bl:1.2:-15:slim")
        b.setDisplay("claude", .all)
        #expect(b.string == "claude:bl:1.2:-15:slim@all")
        #expect(DeskArrangement(b.string) == b)
    }

    @Test func stacksAndTiltSplitByDisplay() {
        var a = DeskArrangement("clock:tl message:tl")
        a.setDisplay("message", .id(uuid))
        #expect(a.stack(.tl).map(\.widget) == ["clock"])
        #expect(a.stack(.tl, on: .id(uuid)).map(\.widget) == ["message"])
        #expect(a.displayStacks().count == 2)
        #expect(Set(a.stacks()[.tl]?.map(\.widget) ?? []) == ["clock", "message"])   // drawing callers stay merged
        a.setTilt("clock", 10)
        a.setTilt("message", -5)
        #expect(a.tilt(of: "clock") == 10 && a.tilt(of: "message") == -5)
        #expect(a.stackTilt(.tl) == 10 && a.stackTilt(.tl, on: .id(uuid)) == -5)
        #expect(a.tiltConflicts.isEmpty)
    }

    @Test func tiltConflictsAreWithinOneDisplay() {
        let split = DeskArrangement.tiltConflicts(displayed: [
            ("clock", .bl, .main, 5), ("claude", .bl, .all, 6)])
        #expect(split.isEmpty)
        let clash = DeskArrangement.tiltConflicts(displayed: [
            ("clock", .bl, .all, 5), ("claude", .bl, .all, 6), ("meters", .bl, .main, 5)])
        #expect(clash == [DeskTiltConflict(anchor: .bl, pieces: ["clock", "claude"], display: .all)])
        // a design word with no display is main
        #expect(DeskArrangement.tiltConflicts([("clock", .bl, 5), ("claude", .bl, 6)]).first?.display == .main)
        let a = DeskArrangement(pieces: [
            DeskPlacement(widget: "clock", anchor: .tl, tilt: 3),
            DeskPlacement(widget: "message", anchor: .tl, tilt: 4, monitor: .all)])
        #expect(a.tiltConflicts.isEmpty)
        let b = DeskArrangement(pieces: [
            DeskPlacement(widget: "clock", anchor: .tl, tilt: 3, monitor: .all),
            DeskPlacement(widget: "message", anchor: .tl, tilt: 4, monitor: .all)])
        #expect(b.tiltConflicts.count == 1)
    }

    @Test func setDisplaySeatsByRankTakesTiltKeepsSizeAndStyle() {
        var a = DeskArrangement("claude:bl:1.4:0:ring clock:tc meters:bl:1:-12:slim@all")
        a.setDisplay("claude", .all)
        // joins the .all stack at bl, takes its -12 tilt, keeps size and style
        let p = a.placement("claude")
        #expect(p == DeskPlacement(widget: "claude", anchor: .bl, scale: 1.4, tilt: -12, style: .ring, monitor: .all))
        #expect(a.stack(.bl, on: .all).map(\.widget) == DeskArrangement.known.filter { ["claude", "meters"].contains($0) })
        a.setDisplay("clock", .id(uuid))   // empty target stack keeps its own tilt
        #expect(a.placement("clock")?.monitor == .id(uuid))
        let before = a
        a.setDisplay("watchers", .all)     // hidden: no-op
        #expect(a == before)
    }

    @Test func placeKeepsTheDisplayAndUsesItsStack() {
        var a = DeskArrangement("clock:bl:1:0:bar@all message:tl")
        a.setTilt("clock", 9)
        a.place("message", at: .bl)         // main's bl is empty: no slant from the .all stack
        #expect(a.placement("message")?.monitor == .main && a.tilt(of: "message") == 0)
        a.place("meters", at: .bl)
        a.setDisplay("meters", .all)
        a.place("meters", at: .tl)
        a.place("meters", at: .bl)
        #expect(a.placement("meters")?.monitor == .all && a.tilt(of: "meters") == 9)
        a.setScale("clock", 1.3)
        a.setStyle("claude", .ring)
        #expect(a.placement("clock")?.monitor == .all)
    }
}
