import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// Item 60: Desk layout, step 2. Arrange mode: which anchor a drop lands on, reordering within a
/// stack, the handle's size steps, the edit (Cancel leaves the saved layout exactly as it was,
/// Done writes once) and the window back to clicks-only-where-drawn afterwards.

/// The Desk suite in memory, counting writes.
private final class CountingDefaults: DefaultsStore {
    var values: [String: Any] = [:]
    var writes: [String] = []
    func object(forKey key: String) -> Any? { values[key] }
    func bool(forKey key: String) -> Bool { values[key] as? Bool ?? false }
    func set(_ value: Any?, forKey key: String) {
        writes.append(key)
        values[key] = value
    }
}

/// A 1512 x 982 screen less 52-point sides, the menu bar plus 40 on top and 60 at the bottom.
private let content = CGRect(x: 52, y: 64, width: 1408, height: 858)

@Suite("Arrange: drag to an anchor")
struct DeskArrangeAnchorTests {
    @Test("each anchor's own point resolves to it")
    func ownPoints() {
        for anchor in DeskAnchor.allCases {
            let p = DeskAnchorGeometry.point(anchor, in: content)
            #expect(DeskArrange.nearest(to: p, in: content) == anchor)
        }
    }

    @Test("a drop near a corner, an edge middle or a center lands there")
    func nearby() {
        #expect(DeskArrange.nearest(to: CGPoint(x: 140, y: 120), in: content) == .tl)
        #expect(DeskArrange.nearest(to: CGPoint(x: 1400, y: 900), in: content) == .br)
        #expect(DeskArrange.nearest(to: CGPoint(x: 760, y: 90), in: content) == .tc)
        #expect(DeskArrange.nearest(to: CGPoint(x: 700, y: 880), in: content) == .bc)
        #expect(DeskArrange.nearest(to: CGPoint(x: 90, y: 500), in: content) == .ml)
        #expect(DeskArrange.nearest(to: CGPoint(x: 1450, y: 470), in: content) == .mr)
        #expect(DeskArrange.nearest(to: CGPoint(x: 1300, y: 140), in: content) == .tr)
        #expect(DeskArrange.nearest(to: CGPoint(x: 200, y: 850), in: content) == .bl)
    }

    @Test("zones are shares of the screen: a wide screen does not shrink the middles")
    func shares() {
        // A third of the way across at mid height is nearer (as shares) the left middle than the top center.
        #expect(DeskArrange.nearest(to: CGPoint(x: content.minX + content.width * 0.3, y: content.midY), in: content) == .ml)
    }

    @Test("the top center below the notch: its point drops and still resolves to it")
    func centerDrop() {
        let p = DeskAnchorGeometry.point(.tc, in: content, centerDrop: 30)
        #expect(p.y == content.minY + 30)
        #expect(DeskArrange.nearest(to: p, in: content, centerDrop: 30) == .tc)
    }

    @Test("an empty content rectangle never divides by zero")
    func empty() {
        _ = DeskArrange.nearest(to: .zero, in: .zero)
    }
}

@Suite("Arrange: reorder and move")
struct DeskArrangeReorderTests {
    let start = "message:tl clock:bl meters:bl meetings:bl"

    @Test("the piece a drop goes in front of: the first drawn piece whose middle is below the pointer")
    func pieceAfter() {
        let stack: [(widget: String, frame: CGRect)] = [
            ("clock", CGRect(x: 0, y: 600, width: 300, height: 140)),
            ("meters", CGRect(x: 0, y: 760, width: 300, height: 80)),
            ("watchers", .zero),
            ("meetings", CGRect(x: 0, y: 860, width: 300, height: 40)),
        ]
        #expect(DeskArrange.pieceAfter(y: 500, in: stack) == "clock")
        #expect(DeskArrange.pieceAfter(y: 700, in: stack) == "meters")
        #expect(DeskArrange.pieceAfter(y: 860, in: stack) == "meetings")
        #expect(DeskArrange.pieceAfter(y: 950, in: stack) == nil)
        #expect(DeskArrange.pieceAfter(y: 0, in: []) == nil)
    }

    /// A tall Bottom left stack on a laptop screen: the clock's top half reaches above the
    /// halfway line between Bottom left (y 922) and Middle left (y 493).
    let tall: [String: CGRect] = [
        "message": CGRect(x: 52, y: 64, width: 400, height: 60),
        "clock": CGRect(x: 52, y: 560, width: 300, height: 140),
        "meters": CGRect(x: 52, y: 720, width: 300, height: 80),
        "meetings": CGRect(x: 52, y: 820, width: 300, height: 80),
    ]

    @Test("a stack's bounds: its drawn pieces' union, grown by the reach; undrawn pieces skipped")
    func stackBounds() {
        let a = DeskArrangement("message:tl clock:bl meters:bl watchers:bl meetings:bl")
        let bounds = DeskArrange.stackBounds(a, frames: tall, reach: 10)
        #expect(bounds[.bl] == CGRect(x: 42, y: 550, width: 320, height: 360))
        #expect(bounds[.tl] == CGRect(x: 42, y: 54, width: 420, height: 80))
        #expect(bounds[.mr] == nil)
    }

    @Test("a drop on the top of a tall stack stays in it and reorders, though Middle left's point is nearer")
    func tallStack() {
        var a = DeskArrangement(start)
        let stacks = DeskArrange.stackBounds(a, frames: tall)
        let drop = CGPoint(x: 150, y: 570)
        #expect(DeskArrange.nearest(to: drop, in: content) == .ml)
        let anchor = DeskArrange.target(point: drop, content: content, stacks: stacks, own: .bl)
        #expect(anchor == .bl)
        let stack = a.stack(anchor).filter { $0.widget != "meetings" }
            .map { (widget: $0.widget, frame: tall[$0.widget] ?? .zero) }
        a.put("meetings", at: anchor, before: DeskArrange.pieceAfter(y: drop.y, in: stack))
        #expect(a.string == "message:tl meetings:bl clock:bl meters:bl")
        // Just above the clock's outline and name label still counts as the stack.
        #expect(DeskArrange.target(point: CGPoint(x: 150, y: 540), content: content, stacks: stacks, own: .bl) == .bl)
    }

    @Test("off every stack the nearest anchor wins; over another stack, that stack")
    func targetFallback() {
        let a = DeskArrangement(start)
        let stacks = DeskArrange.stackBounds(a, frames: tall)
        #expect(DeskArrange.target(point: CGPoint(x: 150, y: 480), content: content, stacks: stacks, own: .bl) == .ml)
        #expect(DeskArrange.target(point: CGPoint(x: 1400, y: 900), content: content, stacks: stacks, own: .bl) == .br)
        #expect(DeskArrange.target(point: CGPoint(x: 300, y: 90), content: content, stacks: stacks, own: .bl) == .tl)
        #expect(DeskArrange.target(point: CGPoint(x: 150, y: 570), content: content, stacks: [:], own: .bl) == .ml)
    }

    @Test("where two stacks' bounds overlap, the dragged piece's own stack wins")
    func ownFirst() {
        let stacks: [DeskAnchor: CGRect] = [.ml: CGRect(x: 40, y: 400, width: 300, height: 300),
                                            .bl: CGRect(x: 40, y: 600, width: 300, height: 330)]
        let p = CGPoint(x: 100, y: 650)
        #expect(DeskArrange.target(point: p, content: content, stacks: stacks, own: .bl) == .bl)
        #expect(DeskArrange.target(point: p, content: content, stacks: stacks, own: .ml) == .ml)
        #expect(DeskArrange.target(point: p, content: content, stacks: stacks, own: .tr) == .ml)
    }

    @Test("within a stack: to the top, to the bottom, between")
    func withinStack() {
        var a = DeskArrangement(start)
        a.put("meetings", at: .bl, before: "clock")
        #expect(a.string == "message:tl meetings:bl clock:bl meters:bl")
        a.put("meetings", at: .bl, before: nil)
        #expect(a.string == "message:tl clock:bl meters:bl meetings:bl")
        a.put("clock", at: .bl, before: "meetings")
        #expect(a.string == "message:tl meters:bl clock:bl meetings:bl")
        #expect(a.stack(.bl).map(\.widget) == ["meters", "clock", "meetings"])
    }

    @Test("to another anchor: into a stack at a place, into an empty one keeping its place in the string")
    func otherAnchor() {
        var a = DeskArrangement(start)
        a.put("meters", at: .tl, before: "message")
        #expect(a.string == "meters:tl message:tl clock:bl meetings:bl")
        var b = DeskArrangement(start)
        b.put("clock", at: .tr, before: nil)
        #expect(b.string == "message:tl clock:tr meters:bl meetings:bl")
        var c = DeskArrangement(start)
        c.put("meetings", at: .tl, before: nil)
        #expect(c.stack(.tl).map(\.widget) == ["message", "meetings"])
    }

    @Test("a piece keeps its size when it moves; an unplaced piece or itself as the target changes nothing")
    func keeps() {
        var a = DeskArrangement("message:tl clock:bl:1.3 meters:bl")
        a.put("clock", at: .mr, before: nil)
        #expect(a.placement("clock") == DeskPlacement(widget: "clock", anchor: .mr, scale: 1.3))
        let before = a
        a.put("watchers", at: .tl, before: nil)
        a.put("meters", at: .bl, before: "meters")
        #expect(a == before)
    }
}

@Suite("Arrange: resize handle")
struct DeskArrangeResizeTests {
    let size = CGSize(width: 300, height: 100)

    @Test("the handle sits on the corner facing the middle of the screen")
    func corners() {
        #expect(DeskArrange.handle(.tl) == (true, true))
        #expect(DeskArrange.handle(.tr) == (false, true))
        #expect(DeskArrange.handle(.bl) == (true, false))
        #expect(DeskArrange.handle(.br) == (false, false))
        #expect(DeskArrange.handle(.ml) == (true, true))
        #expect(DeskArrange.handle(.bc) == (true, false))
    }

    @Test("dragging outward grows, inward shrinks, snapped to the 10% steps")
    func steps() {
        // 300 + 100 = 400 points of span: 40 points outward is 10%.
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: 40, height: 0), anchor: .tl) == 1.1)
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: 20, height: 20), anchor: .tl) == 1.1)
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: -80, height: 0), anchor: .tl) == 0.8)
        // A small drag stays on the step it started on.
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: 10, height: 5), anchor: .tl) == 1)
        // Bottom right grows up and to the left.
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: -40, height: -40), anchor: .br) == 1.2)
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: 40, height: 40), anchor: .br) == 0.8)
        // A center piece grows both ways across: its sideways drag counts twice.
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: 20, height: 0), anchor: .tc) == 1.1)
    }

    @Test("every result is one of Settings' size steps, within 60% to 160%")
    func bounds() {
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: 5000, height: 5000), anchor: .tl) == 1.6)
        #expect(DeskArrange.scale(from: 1, size: size, drag: CGSize(width: -5000, height: 0), anchor: .tl) == 0.6)
        #expect(DeskArrange.scale(from: 1.4, size: .zero, drag: CGSize(width: 50, height: 0), anchor: .tl) == 1.4)
        for dx in stride(from: -400.0, through: 400.0, by: 7.0) {
            let s = DeskArrange.scale(from: 1.2, size: size, drag: CGSize(width: dx, height: dx / 3), anchor: .ml)
            #expect(DeskArrangement.scaleSteps.contains(s))
        }
    }

    @Test("no snapping animation with Reduce Motion")
    func reduceMotion() {
        #expect(DeskArrange.snapAnimation(reduceMotion: true) == nil)
        #expect(DeskArrange.snapAnimation(reduceMotion: false) != nil)
    }
}

@Suite("Arrange: the edit")
struct DeskArrangeEditTests {
    @Test("nothing is saved while arranging; Done writes the layout once")
    func doneWritesOnce() {
        let d = CountingDefaults()
        d.values["layout"] = "message:tl clock:bl meters:bl meetings:bl"
        let mode = DeskArrangeMode(store: d)
        mode.begin()
        #expect(mode.active)
        mode.edit { $0.put("meetings", at: .bl, before: "clock") }
        mode.edit { $0.setScale("clock", 1.2) }
        mode.edit { $0.put("meters", at: .mr, before: nil) }
        #expect(d.writes.isEmpty)
        #expect(d.values["layout"] as? String == "message:tl clock:bl meters:bl meetings:bl")
        #expect(mode.session?.changed == true)
        #expect(mode.done())
        #expect(d.writes == ["layout"])
        #expect(d.values["layout"] as? String == "message:tl meetings:bl clock:bl:1.2 meters:mr")
        #expect(!mode.active)
        // Done again, out of Arrange mode: nothing more.
        #expect(!mode.done())
        #expect(d.writes.count == 1)
    }

    @Test("Cancel leaves the saved string exactly as it was, an older or odd one included")
    func cancelRestores() {
        let saved = "message:tl clock:zz claude:bl meetings:bl"
        let d = CountingDefaults()
        d.values["layout"] = saved
        let mode = DeskArrangeMode(store: d)
        mode.begin()
        mode.edit { $0.put("clock", at: .tr, before: nil) }
        mode.edit { $0.setScale("message", 1.5) }
        #expect(mode.working?.string != saved)
        mode.cancel()
        #expect(!mode.active)
        #expect(mode.working == nil)
        #expect(d.writes.isEmpty)
        #expect(d.values["layout"] as? String == saved)
    }

    @Test("Done with nothing changed writes nothing, so an older string stays readable by older builds")
    func doneUnchanged() {
        let saved = "message:tl clock:zz claude:bl meetings:bl"
        let d = CountingDefaults()
        d.values["layout"] = saved
        let mode = DeskArrangeMode(store: d)
        mode.begin()
        // Moved away and back: the same layout.
        mode.edit { $0.put("meetings", at: .tl, before: nil) }
        mode.edit { $0.put("meetings", at: .bl, before: nil) }
        #expect(mode.session?.changed == false)
        #expect(!mode.done())
        #expect(d.writes.isEmpty)
        #expect(d.values["layout"] as? String == saved)
    }

    @Test("no saved layout: the standard one is edited")
    func standard() {
        let d = CountingDefaults()
        let mode = DeskArrangeMode(store: d)
        mode.begin()
        #expect(mode.session?.saved == DeskLayout.standard)
        mode.cancel()
        #expect(d.values["layout"] == nil)
    }

    @Test("begin while arranging keeps the edit; an edit outside Arrange mode does nothing")
    func guards() {
        let d = CountingDefaults()
        d.values["layout"] = "message:tl clock:bl"
        let mode = DeskArrangeMode(store: d)
        mode.edit { $0.put("clock", at: .tr, before: nil) }
        #expect(mode.working == nil)
        mode.begin()
        mode.edit { $0.put("clock", at: .tr, before: nil) }
        d.values["layout"] = "message:br"
        mode.begin()
        #expect(mode.working?.string == "message:tl clock:tr")
        mode.cancel()
    }

    @Test("the smoke's own edit: the clock to Top right at 120%, unsaved")
    func smokeEdit() {
        let d = CountingDefaults()
        d.values["layout"] = "message:tl clock:bl meters:bl meetings:bl"
        let mode = DeskArrangeMode(store: d)
        mode.begin()
        mode.smokeEdit()
        #expect(mode.working?.string == "message:tl clock:tr:1.2 meters:bl meetings:bl")
        #expect(d.writes.isEmpty)
        mode.cancel()
    }
}

@Suite("Arrange: clicks")
struct DeskArrangeClickTests {
    @Test("arranging takes the whole frame; after, only the drawn click areas, exactly as before")
    func clickThrough() {
        #expect(DeskArrange.takesMouse(arranging: true, overElement: false))
        #expect(DeskArrange.takesMouse(arranging: true, overElement: true))
        #expect(!DeskArrange.takesMouse(arranging: false, overElement: false))
        #expect(DeskArrange.takesMouse(arranging: false, overElement: true))
        #expect(DeskArrange.clickThrough(arranging: true, windowTakesMouse: true) == "whole")
        #expect(DeskArrange.clickThrough(arranging: false, windowTakesMouse: true) == "drawn")
        #expect(DeskArrange.clickThrough(arranging: false, windowTakesMouse: false) == "drawn")
    }

    @Test("after Done or Cancel the mode is off, so the window lets the mouse through off the pieces")
    func restoredAfter() {
        let d = CountingDefaults()
        let mode = DeskArrangeMode(store: d)
        for keep in [true, false] {
            mode.begin()
            #expect(DeskArrange.takesMouse(arranging: mode.active, overElement: false))
            if keep { mode.done() } else { mode.cancel() }
            #expect(!DeskArrange.takesMouse(arranging: mode.active, overElement: false))
            #expect(mode.drag == nil)
        }
    }

    @Test("Return and Enter are Done, Escape is Cancel; other keys do nothing")
    func keys() {
        #expect(DeskArrange.endKey(36) == .done)    // Return
        #expect(DeskArrange.endKey(76) == .done)    // keypad Enter
        #expect(DeskArrange.endKey(53) == .cancel)  // Escape
        #expect(DeskArrange.endKey(0) == nil)
        #expect(DeskArrange.endKey(49) == nil)      // Space
        #expect(DeskArrangeEnd.done.keep)
        #expect(!DeskArrangeEnd.cancel.keep)
    }

    @Test("a key ends the edit as its button does: Return writes, Escape leaves the layout alone")
    func keysDriveTheEdit() throws {
        let store = CountingDefaults()
        store.values[DeskArrangeMode.layoutKey] = "message:tl clock:bl"
        let mode = DeskArrangeMode(store: store)

        mode.begin()
        mode.smokeEdit()
        let escape = try #require(DeskArrange.endKey(53))
        if escape.keep { mode.done() } else { mode.cancel() }
        #expect(!mode.active)
        #expect(store.writes.isEmpty)
        #expect(store.values[DeskArrangeMode.layoutKey] as? String == "message:tl clock:bl")

        mode.begin()
        mode.smokeEdit()
        let enter = try #require(DeskArrange.endKey(36))
        if enter.keep { mode.done() } else { mode.cancel() }
        #expect(store.writes == ["layout"])
        #expect(store.values[DeskArrangeMode.layoutKey] as? String == "message:tl clock:tr:1.2")
    }

    @Test("the bar's panel sits on the middle of the Desk's screen, on whole points")
    func barOrigin() {
        let visible = CGRect(x: 0, y: 80, width: 1512, height: 862)
        let o = DeskArrange.barOrigin(size: CGSize(width: 336, height: 171), in: visible)
        #expect(o == CGPoint(x: 588, y: 426))
        // A second screen to the left, below the first.
        #expect(DeskArrange.barOrigin(size: CGSize(width: 300, height: 100),
                                      in: CGRect(x: -1920, y: -200, width: 1920, height: 1050))
                == CGPoint(x: -1110, y: 275))
    }

    @Test("the bar and Settings say Return is Done and Escape is Cancel")
    func keyCopy() {
        #expect(DeskArrangeCopy.barKeys == "Return or Done keeps the new layout. Escape or Cancel puts it back.")
        #expect(DeskArrangeCopy.settingsNote.contains("Return or Done keeps the new layout; Escape or Cancel puts it back"))
        #expect(!DeskArrangeCopy.barKeys.contains("Escape or Done"))
    }

    @Test("debug link and state.yaml keys")
    func debug() {
        #expect((try? DebugLink.action("desk-arrange", arg: "start").get()) == .deskArrange(.start))
        #expect((try? DebugLink.action("desk-arrange", arg: "CANCEL").get()) == .deskArrange(.cancel))
        #expect((try? DebugLink.action("desk-arrange", arg: nil).get()) == nil)
        #expect(DebugAction.names.contains("desk-arrange"))
        var input = DebugStateInput()
        #expect(YAMLEmitter.emit(DebugState.yaml(input)).contains(
            "desk_arrange:\n  active: false\n  changed: false\n  working: null\n  click_through: drawn\n  bar_visible: false\n"))
        input.deskArrange = DeskArrangeDebug(active: true, changed: true, working: "message:tl clock:tr:1.2",
                                             clickThrough: "whole", barVisible: true)
        #expect(YAMLEmitter.emit(DebugState.yaml(input)).contains(
            "desk_arrange:\n  active: true\n  changed: true\n  working: \"message:tl clock:tr:1.2\"\n  click_through: whole\n  bar_visible: true\n"))
    }

    @Test("the menu item and the Settings button say Arrange Desk…")
    func copy() {
        #expect(DeskArrangeCopy.menuItem == "Arrange Desk…")
        #expect(DeskArrangeCopy.settingsButton == "Arrange Desk…")
    }
}

/// A window as Arrange mode's stash sees it, without a window server.
@MainActor
private final class FakeWindow: DeskArrangeHideable {
    var isVisible: Bool
    var ordersOut = 0
    var bringsBack = 0

    init(visible: Bool) { isVisible = visible }

    func orderOut(_ sender: Any?) {
        ordersOut += 1
        isVisible = false
    }

    func makeKeyAndOrderFront(_ sender: Any?) {
        bringsBack += 1
        isVisible = true
    }
}

@Suite("Arrange: Settings steps aside")
@MainActor
struct DeskArrangeStashTests {
    @Test("an open Settings is put away while arranging and comes back after")
    func settingsComesBack() {
        let settings = FakeWindow(visible: true)
        let stash = DeskArrangeStash()
        stash.hide([settings])
        #expect(!settings.isVisible)
        #expect(settings.ordersOut == 1)
        #expect(stash.hidden.count == 1)
        stash.restore()
        #expect(settings.isVisible)
        #expect(settings.bringsBack == 1)
        #expect(stash.hidden.isEmpty)
        // Restoring again brings nothing back twice.
        stash.restore()
        #expect(settings.bringsBack == 1)
    }

    @Test("a closed or missing Settings stays closed")
    func closedStaysClosed() {
        let settings = FakeWindow(visible: false)
        let stash = DeskArrangeStash()
        stash.hide([settings, nil])
        stash.restore()
        #expect(!settings.isVisible)
        #expect(settings.ordersOut == 0)
        #expect(settings.bringsBack == 0)
    }

    @Test("hiding the same window twice remembers it once")
    func once() {
        let settings = FakeWindow(visible: true)
        let stash = DeskArrangeStash()
        stash.hide([settings])
        settings.isVisible = true
        stash.hide([settings])
        #expect(stash.hidden.count == 1)
    }
}
