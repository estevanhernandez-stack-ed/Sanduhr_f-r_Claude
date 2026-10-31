import Foundation
import Testing
@testable import Sanduhr

/// "New sprite from an image..." (spec 2.39.0 section 18): the choices behind the sheet, and the install through
/// `WidgetsModel`. The drawing is a live check.
@Suite("New sprite model")
@MainActor
struct NewSpriteModelTests {
    /// 32x16 at 8x8 cells: 4 columns, 2 rows, 8 cells.
    private func model(_ w: Int = 32, _ h: Int = 16, install: @escaping (WidgetNewSprite.Input) -> [String] = { _ in [] }) -> NewSpriteModel {
        NewSpriteModel(png: WidgetOwnArtTests.sheet(w, h), install: install)
    }

    private func at8(_ m: NewSpriteModel) -> NewSpriteModel {
        m.setCell(width: 8, height: 8)
        return m
    }

    @Test func theCellCountFollowsTheImageAndTheCellSize() {
        let m = at8(model())
        #expect(m.columns == 4 && m.rows == 2 && m.cellCount == 8 && m.gridFits)
        m.setCell(width: 16, height: 16)
        #expect(m.columns == 2 && m.rows == 1 && m.cellCount == 2)
        m.setCell(width: 12, height: 8)
        #expect(!m.gridFits)
    }

    @Test func theCellSizeIsClampedToTheRange() {
        let m = model()
        m.setCell(width: 2, height: 500)
        #expect(m.cellWidth == 8 && m.cellHeight == 64)
    }

    @Test func walkPlaysInClickOrderAndAClickAgainUntags() {
        let m = at8(model())
        m.selectedAction = "walk"
        for c in [3, 0, 2] { m.tap(c) }
        #expect(m.cells(of: "walk") == [3, 0, 2])
        #expect(m.order(of: 3, in: "walk") == 1 && m.order(of: 0, in: "walk") == 2 && m.order(of: 2, in: "walk") == 3)
        m.tap(0)
        #expect(m.cells(of: "walk") == [3, 2])
        #expect(m.order(of: 2, in: "walk") == 2 && m.order(of: 0, in: "walk") == nil)
        m.tap(3); m.tap(2)
        #expect(m.cells(of: "walk").isEmpty)
    }

    @Test func aCellCanBeInSeveralActionsAndTapsOutsideTheSheetDoNothing() {
        let m = at8(model())
        m.tap(1)
        m.selectedAction = "sit"
        m.tap(1); m.tap(8); m.tap(-1)
        #expect(m.actions(of: 1) == ["walk", "sit"])
        #expect(m.cells(of: "sit") == [1])
    }

    @Test func untaggedActionsSayWhatHappens() {
        let m = at8(model())
        var notes = Dictionary(uniqueKeysWithValues: m.untagged.map { ($0.action, $0.note) })
        #expect(notes.keys.sorted() == ["cheer", "nap", "sit", "walk", "worry"])
        #expect(notes["nap"] == "uses sit" && notes["cheer"] == "uses sit" && notes["worry"] == "uses sit")
        #expect(notes["walk"] == "needs two or more cells" && notes["sit"] == "needs a cell")
        m.tap(0); m.selectedAction = "sit"; m.tap(1); m.selectedAction = "nap"; m.tap(2)
        notes = Dictionary(uniqueKeysWithValues: m.untagged.map { ($0.action, $0.note) })
        #expect(notes.keys.sorted() == ["cheer", "walk", "worry"])
        #expect(notes["walk"] == "needs two or more cells")
    }

    @Test func aSmallerGridDropsTheTagsPastItsLastCell() {
        let m = at8(model())
        for c in [0, 5, 7] { m.tap(c) }
        m.selectedAction = "sit"
        m.tap(6)
        m.setCell(width: 16, height: 16)
        #expect(m.cellCount == 2)
        #expect(m.cells(of: "walk") == [0])
        #expect(m.cells(of: "sit").isEmpty)
        #expect(m.untagged.contains { $0.action == "sit" })
    }

    @Test func theInputCarriesTheChoices() {
        let m = at8(model())
        for c in [2, 1] { m.tap(c) }
        m.selectedAction = "sit"; m.tap(0)
        m.name = "  Blob "
        m.scale = 3
        let i = m.input
        #expect(i.png == m.png && i.cellWidth == 8 && i.cellHeight == 8 && i.name == "  Blob " && i.scale == 3)
        #expect(i.tags == ["walk": [2, 1], "sit": [0]])
    }

    @Test func makeCallsInstallWithTheInputAndClosesOnSuccess() {
        var seen: [WidgetNewSprite.Input] = []
        let m = at8(model(install: { seen.append($0); return [] }))
        m.name = "Blob"
        m.make()
        #expect(seen.count == 1 && seen[0].name == "Blob")
        #expect(m.made && m.errors.isEmpty)
    }

    @Test func errorSentencesPassThroughAndNothingIsMade() {
        let m = at8(model(install: { _ in ["Give the sprite a name.", "Tag at least one cell as sit."] }))
        m.make()
        #expect(!m.made)
        #expect(m.errors == ["Give the sprite a name.", "Tag at least one cell as sit."])
        m.tap(0)
        #expect(m.errors.isEmpty)
    }

    @Test func aBadImageIsReportedAndNotDrawn() {
        let m = NewSpriteModel(png: Data([1, 2, 3]), install: { _ in [] })
        #expect(m.imageError != nil && m.cellCount == 0)
    }

    @Test func theStartingCellSizeIsOneThatFits() {
        #expect(NewSpriteModel.startingCell(64, 32) == 16)
        #expect(NewSpriteModel.startingCell(24, 24) == 24)
        #expect(NewSpriteModel.startingCell(8, 8) == 8)
        #expect(NewSpriteModel.startingCell(30, 7) == 16)
    }

    @Test func aClickMapsToTheCellUnderIt() {
        let m = at8(model())
        #expect(NewSpriteLayout.cell(at: CGPoint(x: 1, y: 1), zoom: 4, model: m) == 0)
        #expect(NewSpriteLayout.cell(at: CGPoint(x: 8 * 4 * 2 + 1, y: 8 * 4 + 1), zoom: 4, model: m) == 6)
        #expect(NewSpriteLayout.cell(at: CGPoint(x: 8 * 4 * 4 + 1, y: 1), zoom: 4, model: m) == -1)
        #expect(NewSpriteLayout.rect(of: 6, zoom: 4, model: m) == CGRect(x: 64, y: 32, width: 32, height: 32))
    }

    // MARK: Install through WidgetsModel

    private func widgets(_ root: WidgetTestRoot) -> WidgetsModel {
        WidgetsModel(store: root.store(), defaults: WidgetScratchDefaults().defaults,
                     makeClient: { WidgetCatalogClient(fetch: { _, _ in nil }, marker: $0) }, changed: {})
    }

    @Test func installingMakesALocalSpriteTheCompanionCanUse() throws {
        let root = WidgetTestRoot()
        let w = widgets(root)
        let m = NewSpriteModel(png: WidgetOwnArtTests.sheet(32, 16), install: { w.installNewSprite($0) })
        m.setCell(width: 8, height: 8)
        for c in [0, 1, 2] { m.tap(c) }
        m.selectedAction = "sit"; m.tap(4)
        m.name = "My Blob"
        m.make()
        #expect(m.made, "\(m.errors)")
        let row = try #require(w.installed.first { $0.id == "my-my-blob" })
        #expect(row.kind == "sprite" && row.madeHere)
        #expect(root.store().load().find("my-my-blob")?.source == .local)
        #expect(w.ownArtStatus?.contains("stays on this Mac") == true)
    }

    @Test func aSecondSpriteWithTheSameNameTakesASuffix() {
        let root = WidgetTestRoot()
        let w = widgets(root)
        for _ in 0..<2 {
            let m = NewSpriteModel(png: WidgetOwnArtTests.sheet(32, 16), install: { w.installNewSprite($0) })
            m.setCell(width: 8, height: 8)
            m.tap(0); m.tap(1); m.selectedAction = "sit"; m.tap(2)
            m.name = "Blob"
            m.make()
            #expect(m.made)
        }
        #expect(Set(w.installed.map(\.id)) == ["my-blob", "my-blob-2"])
    }

    @Test func theSentencesFromMakeReachTheSheetAndNothingIsInstalled() {
        let root = WidgetTestRoot()
        let w = widgets(root)
        let m = NewSpriteModel(png: WidgetOwnArtTests.sheet(32, 16), install: { w.installNewSprite($0) })
        m.setCell(width: 8, height: 8)
        m.name = "Blob"
        m.make()
        #expect(!m.made)
        #expect(m.errors.joined(separator: " ").contains("Tag at least one cell as sit"))
        #expect(w.installed.isEmpty)
    }
}
