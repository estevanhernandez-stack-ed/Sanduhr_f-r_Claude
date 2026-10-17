import AppKit
import Foundation
import Testing
@testable import Sanduhr

/// "Keep the Desk under desktop icons" (DeskLayer): the default, the window level for each
/// choice, the key's round trip and the words. In-memory defaults only.
@Suite("Desk layer")
struct DeskLayerTests {
    @Test func anAbsentKeyReadsOffSoTheDeskSitsWhereItDid() {
        let desk = MemoryDefaults()
        #expect(DeskLayer.isOn(in: desk) == false)
        #expect(DeskLayer.defaultOn == false)
        #expect(DeskLayer.key == "underIcons")
    }

    @Test func theLevelIsJustBelowNormalOrJustAboveTheDesktop() {
        #expect(DeskLayer.level(underIcons: false).rawValue == NSWindow.Level.normal.rawValue - 1)
        let under = DeskLayer.level(underIcons: true).rawValue
        let desktop = Int(CGWindowLevelForKey(.desktopWindow))
        let icons = Int(CGWindowLevelForKey(.desktopIconWindow))
        #expect(under == desktop + 1)
        #expect(under > desktop && under < icons)
    }

    @Test func theSwitchRoundTrips() {
        let desk = MemoryDefaults()
        DeskLayer.set(true, in: desk)
        #expect(DeskLayer.isOn(in: desk))
        DeskLayer.set(false, in: desk)
        #expect(DeskLayer.isOn(in: desk) == false)
    }

    @Test func aFreshInstallLeavesItOff() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        #expect(s.firstRun(widget: widget) == .fresh)
        #expect(s.defaults.object(forKey: "underIcons") == nil)
        #expect(DeskLayer.isOn(in: s.defaults) == false)
    }

    @Test func theWordsSayTheCostAndSearchFindsIt() {
        #expect(DeskLayer.title == "Keep the Desk under desktop icons")
        #expect(DeskLayer.caption.contains("takes no clicks"))
        #expect(SettingsSearch.hits("under desktop icons").contains { $0.entry.page == .deskLayout })
        #expect(SettingsSearch.hits("bottom").contains { $0.entry.page == .deskLayout })
    }
}
