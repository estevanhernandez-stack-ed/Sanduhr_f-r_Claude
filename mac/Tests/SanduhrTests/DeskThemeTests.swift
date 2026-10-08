import SwiftUI
import Testing
@testable import Sanduhr

/// The Match Desk theme: Desk's ink spec mapped to a widget palette, the drop shadow, and the
/// theme resolved from Desk's settings. Desk's settings come from an in-memory store.

@Suite("Match Desk theme")
struct DeskThemeTests {
    func desk(_ values: [String: Any]) -> MemoryDefaults {
        let d = MemoryDefaults()
        d.values = values
        return d
    }

    @Test func singleColorDoublesAndColorsTheText() {
        let p = DeskThemeMapping.palette(ink: "9ad7ff", shadow: true)
        let ink = Color.hex("9ad7ff")
        #expect(p.ink?.stops == [ink, ink])
        #expect(p.text == ink)
        #expect(p.textSecondary == ink.opacity(0.85))
        #expect(p.textDim == ink.opacity(0.7))
        #expect(p.accent == ink)
        #expect(p.paceMarker == ink)
        #expect(p.barBg == ink.opacity(0.22))
    }

    @Test func gradientRunsLeftToRight() {
        let p = DeskThemeMapping.palette(ink: "531b93, 012089,#00FDFF", shadow: true)
        #expect(p.ink?.stops == [Color.hex("531b93"), Color.hex("012089"), Color.hex("00fdff")])
        #expect(p.text == Color.hex("531b93"))
        #expect(p.accent == Color.hex("012089"))
        #expect(p.sparkline == Color.hex("012089"))
        #expect(DeskThemeMapping.inkHexes("531b93,00fdff") == ["531b93", "00fdff"])
    }

    @Test func emptyOrInvalidInkFallsBackToWhite() {
        #expect(DeskThemeMapping.inkHexes("") == ["ffffff", "ffffff"])
        #expect(DeskThemeMapping.inkHexes(" , ") == ["ffffff", "ffffff"])
        #expect(DeskThemeMapping.inkHexes("zzz,#12,1234567") == ["ffffff", "ffffff"])
        // A bad stop is skipped; the good one stands alone, doubled.
        #expect(DeskThemeMapping.inkHexes("nope, #0AF") == ["00aaff", "00aaff"])
        let p = DeskThemeMapping.palette(ink: "not a color", shadow: true)
        #expect(p.text == Color.hex("ffffff"))
    }

    @Test func noGlassNoCardsNoBorders() {
        let p = DeskThemeMapping.palette(ink: "ffffff", shadow: true)
        #expect([p.bg, p.glass, p.titleBg, p.footerBg, p.glassOnMica, p.border] == Array(repeating: Color.clear, count: 6))
        #expect(p.overlayOpacity == 0)
        #expect(p.glassAlpha == 0)
        #expect(p.borderAlpha == 0)
        #expect(p.innerHighlight == nil)
        #expect(p.accentBloom.alpha == 0)
    }

    @Test func shadowFlagCarriesToTheWidgetShadow() {
        let on = DeskThemeMapping.palette(ink: "ffffff", shadow: true)
        let off = DeskThemeMapping.palette(ink: "ffffff", shadow: false)
        #expect(on.ink?.shadow == true)
        #expect(off.ink?.shadow == false)
        #expect(WidgetShadow.resolve(subtle: true, ink: on.ink) == WidgetShadow(opacity: 0.45, radius: 4, y: 2))
        #expect(WidgetShadow.resolve(subtle: true, ink: off.ink).opacity == 0)
        // Other themes keep their shadows.
        #expect(WidgetShadow.resolve(subtle: false, ink: nil) == WidgetShadow(opacity: 0.45, radius: 24, y: 10))
        #expect(WidgetShadow.resolve(subtle: true, ink: nil) == WidgetShadow(opacity: 0.6, radius: 3, y: 1))
    }

    @Test func warningsKeepTheUsageColors() {
        #expect(DeskThemeMapping.inkCarries(0))
        #expect(DeskThemeMapping.inkCarries(74.9))
        #expect(!DeskThemeMapping.inkCarries(75))
        #expect(!DeskThemeMapping.inkCarries(120))
    }

    @Test func builtInAndOtherThemesUntouched() {
        #expect(ThemeRegistry.builtIn.last?.id == "match-desk")
        #expect(ThemeRegistry.builtIn.last?.displayName == "Match Desk")
        #expect(ThemeRegistry.builtIn.filter { $0.palette.ink != nil }.map(\.id) == ["match-desk"])
    }

    @Test func deskLookDefaultsMatchDesk() {
        // No font saved: EsteFont Pro, the Desk's default since item 65 (e).
        #expect(DeskLook.read(desk([:])) == DeskLook(font: "EsteFont Pro", ink: "ffffff", shadow: true))
        #expect(DeskLook.read(desk(["font": ""])).font == "")
        let look = DeskLook.read(desk(["font": "EsteFont", "inkColor": "ff0000", "inkShadow": false]),
                                 available: { _ in true })
        #expect(look == DeskLook(font: "EsteFont", ink: "ff0000", shadow: false))
        // A saved font that is no longer installed: EsteFont Pro.
        #expect(DeskLook.read(desk(["font": "Gone Font"]), available: { _ in false }).font == "EsteFont Pro")
    }

    @Test func matchDeskResolvesFromDeskSettings() {
        let t = UsageViewModel.resolve("match-desk", desk: desk(["inkColor": "ff0000,0000ff", "inkShadow": false]))
        #expect(t?.id == "match-desk")
        #expect(t?.palette.ink?.stops == [Color.hex("ff0000"), Color.hex("0000ff")])
        #expect(t?.palette.ink?.shadow == false)
        // Other ids are the registry's copy; unknown ids resolve to nothing.
        #expect(UsageViewModel.resolve("aurora", desk: desk([:])) == ThemeRegistry.theme(id: "aurora"))
        #expect(UsageViewModel.resolve("gone", desk: desk([:])) == nil)
    }

    @Test func reresolveFollowsDeskAndLeavesOthers() {
        let red = desk(["inkColor": "ff0000"])
        let fresh = UsageViewModel.reresolved(DeskThemeMapping.builtIn, desk: red)
        #expect(fresh?.palette.text == Color.hex("ff0000"))
        // Unchanged: nothing to do.
        #expect(UsageViewModel.reresolved(DeskThemeMapping.theme(ink: "ff0000", shadow: true), desk: red) == nil)
        #expect(UsageViewModel.reresolved(ThemeRegistry.theme(id: "obsidian")!, desk: red) == nil)
        // A theme whose file is gone stays as it is.
        let gone = Theme(id: "gone", displayName: "Gone", palette: ThemeRegistry.builtIn[0].palette)
        #expect(UsageViewModel.reresolved(gone, desk: red) == nil)
    }

    @Test func galleryCardFollowsDeskInk() {
        let items = ThemeGallery.items(builtIns: ThemeRegistry.builtIn, user: [], current: "match-desk")
        let live = ThemeGallery.withLiveDesk(items, ink: "00fdff", shadow: false)
        let card = live.first { $0.id == "match-desk" }
        #expect(card?.theme.palette.text == Color.hex("00fdff"))
        #expect(card?.theme.palette.ink?.shadow == false)
        #expect(card?.isCurrent == true)
        #expect(Array(live.dropLast()) == Array(items.dropLast()))
        // A user theme that took the id is shown as the user wrote it.
        let mine = Theme(id: "match-desk", displayName: "Mine", palette: ThemeRegistry.builtIn[0].palette)
        let overridden = ThemeGallery.items(builtIns: ThemeRegistry.builtIn, user: [mine], current: "")
        #expect(ThemeGallery.withLiveDesk(overridden, ink: "00fdff", shadow: true) == overridden)
    }
}
