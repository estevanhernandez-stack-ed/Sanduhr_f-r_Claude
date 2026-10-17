import Foundation
import Testing
@testable import Sanduhr

/// Mods & Config's first screen (2026-10-17): Meters above the prompt as a header with a row per
/// folder, and More: mods and config under it, folded unless there is something in it for the
/// user. Nothing here reads the real ~/.claude* or UserDefaults.
@Suite("Mods & Config header and More")
@MainActor
struct ModsMoreTests {
    private func row(_ folder: String, listed: Bool = true) -> OwnModRow {
        var state = OwnModState(folder: folder)
        state.entry = listed ? "/mods/sanduhr-meters" : nil
        return OwnModRow(state: state)
    }

    private func kept(_ name: String) -> KeptMod {
        KeptMod(name: name, stamp: "abcdef123456", keptAt: Date(), session: "s", files: 1, bytes: 1)
    }

    @Test func aFreshProfileWithOneFolderShowsTheHeaderAlone() {
        let model = ModsPageModel(moreStore: MemoryDefaults())
        model.own = [row("/a", listed: false)]
        #expect(!model.moreExpanded(demo: false, waiting: 0))
        #expect(!ModsPageModel.moreStartsOpen(kept: 0, waiting: 0, folders: 1))
        // Before the folders are read, nothing is in it either.
        #expect(!ModsPageModel.moreStartsOpen(kept: 0, waiting: 0, folders: 0))
    }

    @Test func aKeptModOrAWaitingRequestOpensMore() {
        let model = ModsPageModel(moreStore: MemoryDefaults())
        model.own = [row("/a")]
        model.kept = [kept("watch")]
        #expect(model.moreExpanded(demo: false, waiting: 0))
        model.kept = []
        #expect(model.moreExpanded(demo: false, waiting: 1))
        #expect(ModsPageModel.moreStartsOpen(kept: 1, waiting: 0, folders: 1))
        #expect(ModsPageModel.moreStartsOpen(kept: 0, waiting: 1, folders: 1))
    }

    @Test func twoFoldersOpenMore() {
        let model = ModsPageModel(moreStore: MemoryDefaults())
        model.own = [row("/a"), row("/b", listed: false)]
        #expect(model.moreExpanded(demo: false, waiting: 0))
        #expect(ModsPageModel.moreStartsOpen(kept: 0, waiting: 0, folders: 2))
    }

    @Test func theChoiceIsRememberedOverTheDefault() {
        let store = MemoryDefaults()
        store.values[ModsPageModel.moreExpandedKey] = false
        let closed = ModsPageModel(moreStore: store)
        closed.own = [row("/a"), row("/b")]
        closed.kept = [kept("watch")]
        // Two folders and a kept mod would open it; the user closed it last time.
        #expect(!closed.moreExpanded(demo: false, waiting: 1))

        closed.setMoreExpanded(true)
        #expect(store.values[ModsPageModel.moreExpandedKey] as? Bool == true)
        let next = ModsPageModel(moreStore: store)
        next.own = [row("/a", listed: false)]
        // Nothing in it, but the user opened it last time.
        #expect(next.moreExpanded(demo: false, waiting: 0))

        // A link into More opens it for the window; closing it by hand is a choice again.
        let fresh = ModsPageModel(moreStore: MemoryDefaults())
        fresh.own = [row("/a", listed: false)]
        fresh.moreOpenedByLink = true
        #expect(fresh.moreExpanded(demo: false, waiting: 0))
        fresh.windowClosed()
        #expect(!fresh.moreExpanded(demo: false, waiting: 0))
        fresh.moreOpenedByLink = true
        fresh.setMoreExpanded(false)
        #expect(!fresh.moreOpenedByLink && !fresh.moreExpanded(demo: false, waiting: 0))
    }

    @Test func theHeaderListsTheFoldersTheStudioTabLists() {
        // MetersHeader and StudioFolders both draw `studioRows(demo:)`: one list, in folder order.
        let model = ModsPageModel(moreStore: MemoryDefaults())
        model.own = [row("/a"), row("/b", listed: false), row("/c")]
        #expect(model.studioRows(demo: false).map(\.folder) == ["/a", "/b", "/c"])
        // No seat is marked work before the seats are read, so demo mode hides none.
        #expect(model.studioRows(demo: true).map(\.folder) == ["/a", "/b", "/c"])
        #expect(model.studioRows(demo: false).map { $0.state.listed } == [true, false, true])
    }
}
