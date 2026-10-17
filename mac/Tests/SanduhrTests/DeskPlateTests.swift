import Foundation
import Testing
@testable import Sanduhr

/// The plate behind each Desk piece (Settings, Desk Look's "Plate behind the text"): the default
/// rule, the first-run write, the key's round trip and the Reduce Transparency style. In-memory
/// defaults only.
@Suite("Desk plate")
struct DeskPlateTests {
    @Test func anAbsentKeyReadsOffSoAnExistingDeskLooksAsItDid() {
        let desk = MemoryDefaults()
        #expect(DeskPlate.isOn(in: desk) == false)
        // The modifier's @AppStorage starts from the same value under the same key, so with the
        // key absent it draws nothing.
        #expect(DeskPlate.defaultOn == false)
        #expect(DeskPlate.key == "plate")
    }

    @Test func aFreshInstallWritesItOn() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        #expect(s.firstRun(widget: widget) == .fresh)
        #expect(s.defaults.object(forKey: "plate") as? Bool == true)
        #expect(DeskPlate.isOn(in: s.defaults))
    }

    @Test func anExistingInstallGetsNoKey() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        widget.set("obsidian", forKey: "theme")
        #expect(s.firstRun(widget: widget) == .existing)
        #expect(s.defaults.object(forKey: "plate") == nil)
        #expect(DeskPlate.isOn(in: s.defaults) == false)
    }

    @Test func theSwitchRoundTrips() {
        let desk = MemoryDefaults()
        DeskPlate.set(true, in: desk)
        #expect(DeskPlate.isOn(in: desk))
        #expect(desk.object(forKey: "plate") as? Bool == true)
        DeskPlate.set(false, in: desk)
        #expect(DeskPlate.isOn(in: desk) == false)
        #expect(desk.object(forKey: "plate") as? Bool == false)
    }

    @Test func reduceTransparencyPicksTheSolidStyle() {
        #expect(DeskPlate.style(reduceTransparency: false) == DeskPlate.Style(opacity: 0.30, blur: 10))
        #expect(DeskPlate.style(reduceTransparency: true) == DeskPlate.Style(opacity: 0.55, blur: 0))
    }

    @Test func theToggleIsNamedCaptionedAndFoundBySearch() {
        #expect(DeskPlate.title == "Plate behind the text")
        #expect(DeskPlate.caption == "A soft dark plate behind each piece, for desktops with files under it.")
        #expect(SettingsSearch.hits("plate").contains { $0.entry.page == .deskLook })
    }
}
