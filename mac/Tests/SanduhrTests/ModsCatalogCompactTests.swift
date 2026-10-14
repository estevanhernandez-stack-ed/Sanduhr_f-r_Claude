import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice A: the Catalog folds each card to one line. A line opens when clicked, or by
/// itself when a search finds the card only by something it brings.
@Suite("Mods & Config: the compact Catalog")
@MainActor
struct ModsCatalogCompactTests {
    private func kit() -> CatalogCard {
        var c = CatalogCard(kind: .plugin, name: "review-kit")
        c.description = "Reviews a branch before it merges."
        c.sources = ["team-tools"]
        var p = PluginContents()
        p.skills = [SeatEntry(name: "security-pass", description: "", scope: .seat, path: "")]
        p.commands = [SeatEntry(name: "ship", description: "", scope: .seat, path: "")]
        c.contents = CatalogContents(p, work: false)
        return c
    }

    @Test func aMatchOnlyInsideOpensTheLine() {
        #expect(ConfigCatalog.matchesInsideOnly(kit(), search: "security"))
        #expect(ConfigCatalog.matchesInsideOnly(kit(), search: "SHIP"))
        // One word outside and one inside: the inside word is only visible with the card open.
        #expect(ConfigCatalog.matchesInsideOnly(kit(), search: "review security"))
    }

    @Test func aMatchOnTheLineItselfLeavesItFolded() {
        #expect(!ConfigCatalog.matchesInsideOnly(kit(), search: "review"))
        #expect(!ConfigCatalog.matchesInsideOnly(kit(), search: "branch"))
        #expect(!ConfigCatalog.matchesInsideOnly(kit(), search: "team-tools"))
        #expect(!ConfigCatalog.matchesInsideOnly(kit(), search: ""))
        #expect(!ConfigCatalog.matchesInsideOnly(kit(), search: "nothing-like-it"))
        #expect(!ConfigCatalog.matchesInsideOnly(CatalogCard(kind: .skill, name: "solo"), search: "security"))
    }

    @Test func linesOpenAndFoldAndSearchOpensTheirContents() {
        let model = ModsPageModel()
        let card = kit()
        #expect(!model.isOpen(card) && !model.showsContents(card))
        model.toggleCard(card.id)
        #expect(model.isOpen(card) && !model.showsContents(card))
        model.toggleCard(card.id)
        #expect(!model.isOpen(card))
        model.filter.search = "security"
        #expect(model.isOpen(card) && model.showsContents(card))
        model.filter.search = ""
        model.toggleCard(card.id)
        model.collapseAll()
        #expect(!model.isOpen(card) && model.openCards.isEmpty)
    }

    @Test func everyKindHasAnIcon() {
        #expect(Set(CatalogKind.allCases.map(\.symbol)).count == CatalogKind.allCases.count)
    }

    @Test func theSearchMarksEveryWordInTheName() {
        func marked(_ text: String, _ search: String) -> [String] {
            ConfigCatalog.matchRanges(in: text, search: search).map { String(text[$0]) }
        }
        #expect(marked("superpowers-chrome", "chrome") == ["chrome"])
        // Case and accents don't matter; every occurrence of every word.
        #expect(marked("Café Mod café", "CAFE") == ["Café", "café"])
        #expect(marked("plugin-dev tools", "dev tool") == ["dev", "tool"])
        // Overlapping words mark one run.
        #expect(marked("statusline", "status tusl") == ["statusl"])
        #expect(marked("anything", "  ").isEmpty && marked("abc", "zz").isEmpty)
    }

    @MainActor
    @Test func rightOpensALineAndLeftFoldsIt() {
        let model = ModsPageModel()
        model.openCard("a")
        model.openCard("a")
        #expect(model.openCards == ["a"])
        model.foldCard("a")
        model.foldCard("b")
        #expect(model.openCards.isEmpty)
    }
}
