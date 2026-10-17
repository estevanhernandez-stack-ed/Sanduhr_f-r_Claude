import Foundation
import Testing
@testable import Sanduhr

/// Polish 2026-10-17 (#14 to #16): Mods & Config's controls appear when they apply. The rule is a
/// pure function of the catalog's size and the folder count; nothing here reads a folder.
@Suite("Mods & Config: controls when they apply")
struct ModsControlsTests {
    @Test func aFreshProfileSeesSearchAndTheCardsAlone() {
        // One folder, the stock mod: no filter row, sort, Expand/Collapse, chip picker or Compare.
        let fresh = ModsControls(cards: 1, folders: 1)
        #expect(!fresh.toolbar && !fresh.picker && !fresh.compare)
        // Before the folders are read, nothing either.
        let unread = ModsControls(cards: 0, folders: 0)
        #expect(!unread.toolbar && !unread.picker && !unread.compare)
    }

    @Test func twoFoldersShowEverything() {
        let two = ModsControls(cards: 1, folders: 2)
        #expect(two.toolbar && two.picker && two.compare)
        #expect(ModsControls.pickerFolderThreshold == 2)
    }

    @Test func aFullCatalogShowsTheToolbarButNotTheFolderControls() {
        // More than the threshold's cards in one folder: filters, sort and Expand/Collapse, and
        // still no Folder filter, chip picker or Compare, which need a second folder.
        let at = ModsControls(cards: ModsControls.toolbarCardThreshold, folders: 1)
        #expect(!at.toolbar)
        let over = ModsControls(cards: ModsControls.toolbarCardThreshold + 1, folders: 1)
        #expect(over.toolbar && !over.picker && !over.compare)
        #expect(ModsControls.toolbarCardThreshold == 10)
    }

    @Test func aSetFilterKeepsTheToolbarSoItCanBeCleared() {
        // Demo mode can hide a work folder after a filter was chosen: the toolbar stays.
        let narrowed = ModsControls(cards: 1, folders: 1, narrowed: true)
        #expect(narrowed.toolbar && !narrowed.picker && !narrowed.compare)
        var filter = CatalogFilter()
        #expect(!filter.narrows)
        filter.search = "lint"
        #expect(!filter.narrows)
        filter.show = .needsLook
        #expect(filter.narrows)
    }

    @Test @MainActor func theTabBarDropsCompareUntilTwoFoldersAndTheHookStillLandsOnIt() {
        let model = ModsPageModel(moreStore: MemoryDefaults())
        #expect(model.tabs(demo: false) == [.catalog, .bySeat])
        // The smoke hook asks for Compare on a one-folder Mac: the tab switches, the view says
        // what it needs, and the case stays.
        model.showTab(.compare, seat: nil)
        #expect(model.tab == .compare && ModsTab.allCases.contains(.compare))
    }
}
