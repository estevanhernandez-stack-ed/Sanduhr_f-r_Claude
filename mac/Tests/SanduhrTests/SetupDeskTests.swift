import Foundation
import Testing
@testable import Sanduhr

/// "A nicer desktop" off leaves only the drawers (Set up Sanduhr, item 3). In-memory stores only.
@Suite("Set up Sanduhr desktop off")
struct SetupDeskTests {
    private let words = "message:tl clock:bl claude:bl meters:tr weather:br nowPlaying:bc watchers:tc companion:ml"

    @Test func offRemovesEveryPiece() {
        let all = DeskArrangement(words)
        #expect(all.pieces.count >= 7)
        #expect(SetupDesk.visible(all, desktop: false).pieces.isEmpty)
        #expect(all.pieces.allSatisfy { !SetupDesk.shows($0.widget, desktop: false) })
        #expect(SetupDesk.visibleWords(words, desktop: false).isEmpty)
    }

    @Test func onKeepsEveryPiece() {
        let all = DeskArrangement(words)
        #expect(SetupDesk.visible(all, desktop: true) == all)
        #expect(SetupDesk.visibleWords(words, desktop: true) == words)
        #expect(all.pieces.allSatisfy { SetupDesk.shows($0.widget, desktop: true) })
    }

    @Test func theStoredLayoutIsUntouchedByToggling() {
        let store = MemoryDefaults()
        store.set(words, forKey: "layout")
        for desktop in [false, true, false, true] {
            SetupStore.save(SetupSettings(desktop: desktop, usage: ["claude": true]), to: store)
            #expect(SetupDesk.desktopOn(from: store) == desktop)
            _ = SetupDesk.visible(DeskArrangement(store.string(forKey: "layout") ?? ""), desktop: desktop)
            #expect(store.string(forKey: "layout") == words)
        }
        #expect(SetupDesk.visible(DeskArrangement(words), desktop: true).string == DeskArrangement(words).string)
    }

    @Test func noStoredSetupReadsAsDesktopOn() {
        #expect(SetupDesk.desktopOn(from: MemoryDefaults()))
    }

    @Test func theLinesAppearOnlyWhileDesktopIsOff() {
        #expect(SetupDesk.arrangeNote(desktop: true) == nil)
        #expect(SetupDesk.arrangeNote(desktop: false) == "Turn on A nicer desktop in Set up Sanduhr to arrange the Desk.")
        #expect(SetupDesk.hiddenNote(desktop: false, deskOn: true) == "Hidden while A nicer desktop is off. Your places and order are kept.")
        #expect(SetupDesk.hiddenNote(desktop: true, deskOn: true) == nil)
        #expect(SetupDesk.hiddenNote(desktop: false, deskOn: false) == nil, "a Desk switched off says so itself")
        #expect(SetupDesk.designNote(desktop: true) == nil)
        #expect(SetupDesk.designNote(desktop: false) == "A nicer desktop is off, so Apply saves this for when you turn it on.")
    }

    @Test func theReviewModelCarriesTheApplyLine() {
        func model(off: Bool) -> DeskDesignReviewModel {
            DeskDesignReviewModel(id: "a", heading: "h", name: "n", note: nil, changesTitle: "c", changes: [],
                                  missingFonts: [], deskOff: false, previewing: false, desktopOff: off)
        }
        #expect(model(off: false).applyNote == nil)
        #expect(model(off: true).applyNote == SetupCopy.designDesktopOffNote)
        #expect(model(off: true).canApply)
    }

    @Test func newCopyHasNoEmDash() {
        #expect(!SetupCopy.arrangeNeedsDesktop.contains("\u{2014}"))
        #expect(!SetupCopy.designDesktopOffNote.contains("\u{2014}"))
    }
}
