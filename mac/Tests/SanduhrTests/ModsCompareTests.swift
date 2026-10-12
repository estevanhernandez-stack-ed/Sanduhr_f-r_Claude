import Foundation
import Testing
@testable import Sanduhr

/// Item 71, slice 1d: two seats side by side, from the Catalog's cards. Made-up seats only.
@Suite("Mods & Config: Compare")
struct ModsCompareTests {
    static func card(_ kind: CatalogKind, _ name: String, _ chips: [(String, SeatMark, CatalogChipState)]) -> CatalogCard {
        var c = CatalogCard(kind: kind, name: name)
        c.chips = chips.map { CatalogChip(seat: $0.0, label: $0.0, mark: $0.1, state: $0.2) }
        return c
    }

    @Test func sortsEveryCardIntoOneBucket() {
        let cards = [
            Self.card(.plugin, "both-on", [("/a", .work, .on), ("/b", .personal, .on)]),
            Self.card(.plugin, "on-off", [("/a", .work, .on), ("/b", .personal, .off)]),
            Self.card(.plugin, "projects", [("/a", .work, .projects(2)), ("/b", .personal, .projects(1))]),
            Self.card(.skill, "a-only", [("/a", .work, .on)]),
            Self.card(.agent, "b-only", [("/b", .personal, .on)]),
            Self.card(.mcpServer, "elsewhere", [("/c", .personal, .on)]),
            Self.card(.plugin, "available", []),
        ]
        let cmp = ConfigCatalog.compare(cards, a: "/a", b: "/b")
        #expect(cmp.onlyA.map(\.card.name) == ["a-only"])
        #expect(cmp.onlyB.map(\.card.name) == ["b-only"])
        #expect(cmp.differ.map(\.card.name) == ["on-off", "projects"])
        #expect(cmp.same.map(\.card.name) == ["both-on"])
        #expect(cmp.differ[0].a == .on && cmp.differ[0].b == .off)
        #expect(cmp.counts == [1, 1, 2, 1])
        // Swapping the seats swaps the buckets.
        let back = ConfigCatalog.compare(cards, a: "/b", b: "/a")
        #expect(back.onlyA.map(\.card.name) == ["b-only"] && back.onlyB.map(\.card.name) == ["a-only"])
        #expect(!SeatComparison.Bucket.same.startsOpen && SeatComparison.Bucket.onlyA.startsOpen)
    }

    @Test func demoModeComparesWithoutAWorkSeatsCards() {
        let cards = [
            Self.card(.plugin, "work-only", [("/a", .work, .on)]),
            Self.card(.plugin, "shared", [("/a", .work, .on), ("/b", .personal, .on), ("/c", .personal, .off)]),
        ]
        let shown = cards.compactMap { $0.demoView() }
        let cmp = ConfigCatalog.compare(shown, a: "/b", b: "/c")
        #expect(cmp.differ.map(\.card.name) == ["shared"])
        #expect(cmp.counts == [0, 0, 1, 0])
        #expect(!shown.flatMap(\.chips).contains { $0.mark == .work })
    }

    @Test func theSmokeHookOpensCompare() {
        #expect((try? DebugLink.action("mods-config", arg: "compare").get()) == .modsConfig(.compare, seat: nil))
        #expect(ModsTab.compare.debugName == "compare" && ModsTab.allCases.count == 3)
    }
}
